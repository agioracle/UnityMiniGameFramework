using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    public sealed class Scope : IScope, IRetryableCleanupRegistry, IDiagnosticSource
    {
        private readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        private readonly CancellationToken token;
        private readonly CleanupRegistry registry=new CleanupRegistry();
        private readonly List<Scope> children=new List<Scope>();
        private readonly Dictionary<string, SubscriptionCleanup> subscriptions=new Dictionary<string, SubscriptionCleanup>();
        private readonly List<FrameworkError> taskDiagnostics=new List<FrameworkError>();
        private int pendingTasks;
        private bool closing, disposed, cancellationStarted, cancellationFinished;
        private FrameworkError? cancelError;
        public string Id { get; }
        public ScopeKind Kind { get; }
        public IScope Parent { get; }
        public CancellationToken LifetimeCancellation => token;
        public IRetryableCleanupRegistry Cleanup => this;
        public IReadOnlyList<FrameworkError> TaskDiagnostics => taskDiagnostics.AsReadOnly();
        public ScopeState State
        {
            get
            {
                CleanupWait.MainThread();
                if(!closing) return ScopeState.Open;
                var report=Snapshot;
                return report.IsFullyDrained ? ScopeState.Closed : report.PendingCount>0 ? ScopeState.Closing : ScopeState.CleanupFailed;
            }
        }
        CleanupRegistryState ICleanupRegistry.State => registry.State;
        public Result<ICleanupTicket> Register(IAsyncCleanup cleanup) => registry.Register(cleanup);
        public CleanupReport Snapshot
        {
            get
            {
                var report=registry.Snapshot;
                // A sealed, empty registry is not a fully closed scope while cancellation
                // callbacks are still traversing this subtree. Reads must not dispose its CTS.
                if(closing && !cancellationFinished) return new CleanupReport(report.PendingCount+1,report.FailedCount,false);
                if(report.IsFullyDrained && !cancelError.HasValue && !disposed)
                { disposed=true; lifetime.Dispose(); children.Clear(); subscriptions.Clear(); }
                return cancelError.HasValue ? new CleanupReport(report.PendingCount,report.FailedCount+1,false) : report;
            }
        }
        public DiagnosticCounters ReadDiagnostics()
        {
            CleanupWait.MainThread();var state=State;
            var result=new DiagnosticCounters{ActiveScopes=state==ScopeState.Closed?0:1,PendingOperations=pendingTasks,FailedCleanup=registry.Snapshot.FailedCount+(cancelError.HasValue?1:0)};
            foreach(var child in children)result.Add(child.ReadDiagnostics());
            return result;
        }
        public Scope(string id,ScopeKind kind=ScopeKind.App) : this(id,kind,null) { }
        private Scope(string id,ScopeKind kind,Scope parent)
        {
            CleanupWait.MainThread();
            if(string.IsNullOrWhiteSpace(id) || !Enum.IsDefined(typeof(ScopeKind),kind)) throw new ArgumentException("Valid scope id/kind required.");
            Id=id;Kind=kind;Parent=parent;token=lifetime.Token;
        }
        public Result<IScope> CreateChild(string id,ScopeKind kind)
        {
            CleanupWait.MainThread();
            if(closing) return Result<IScope>.Failure(new FrameworkError(ErrorKind.InvalidState,"scope.closed"));
            if(string.IsNullOrWhiteSpace(id) || !Enum.IsDefined(typeof(ScopeKind),kind)) return Result<IScope>.Failure(new FrameworkError(ErrorKind.InvalidArgument,"scope.child.invalid"));
            var child=new Scope(id,kind,this);
            var registered=registry.Register(new ChildCleanup(child));
            if(!registered.IsSuccess) { child.lifetime.Dispose();return Result<IScope>.Failure(registered.Error); }
            children.Add(child);return Result<IScope>.Success(child);
        }
        public Result<ICleanupTicket> TrackSubscription(string id,IDisposable subscription)
        {
            CleanupWait.MainThread();
            if(closing) return Rejected("scope.closed",ErrorKind.InvalidState);
            if(string.IsNullOrWhiteSpace(id) || subscription==null) return Rejected("scope.subscription.invalid",ErrorKind.InvalidArgument);
            if(subscriptions.TryGetValue(id,out var existing))
                return ReferenceEquals(existing.Subscription,subscription) ? registry.Register(existing) : Rejected("scope.subscription.duplicate",ErrorKind.InvalidArgument);
            foreach(var owned in subscriptions.Values)
                if(ReferenceEquals(owned.Subscription,subscription)) return Rejected("scope.subscription.already.owned",ErrorKind.InvalidArgument);
            var cleanup=new SubscriptionCleanup(id,subscription);var result=registry.Register(cleanup);
            if(result.IsSuccess) subscriptions.Add(id,cleanup);
            return result;
        }
        public Result<ICleanupTicket> RunTask(string id,Func<CancellationToken,UniTask<Result>> factory)
        {
            CleanupWait.MainThread();
            if(closing) return Rejected("scope.closed",ErrorKind.InvalidState);
            if(factory==null) return Rejected("scope.task.invalid",ErrorKind.InvalidArgument);
            var cleanup=new TaskCleanup(id,this);var registered=registry.Register(cleanup);
            if(registered.IsSuccess) { pendingTasks++;cleanup.Start(factory,token); }
            return registered;
        }
        private static Result<ICleanupTicket> Rejected(string code,ErrorKind kind) => Result<ICleanupTicket>.Failure(new FrameworkError(kind,code));
        internal void GateTree()
        {
            if(closing) return;
            closing=true; registry.Seal();
            // Gate the entire subtree before callbacks can reenter another child.
            foreach(var child in children.ToArray()) child.GateTree();
        }
        internal void CancelTree()
        {
            if(cancellationStarted) return;
            cancellationStarted=true;
            try
            {
                foreach(var child in children.ToArray()) child.CancelTree();
                if(disposed || lifetime.IsCancellationRequested) return;
                try { lifetime.Cancel(); }
                catch(Exception error) { cancelError=new FrameworkError(ErrorKind.CleanupFailed,"scope.cancel.callback",error.GetType().Name); }
            }
            finally { cancellationFinished=true; }
        }
        public async UniTask<CleanupReport> CloseAsync(CleanupContext cleanup)
        {
            CleanupWait.MainThread();GateTree();CancelTree();
            await registry.CloseAsync(cleanup);
            return Snapshot;
        }
        public async UniTask<CleanupReport> RetryFailedAsync(CleanupContext cleanup)
        {
            CleanupWait.MainThread();
            if(!closing) return Snapshot;
            await registry.RetryFailedAsync(cleanup);
            return Snapshot;
        }
        private sealed class ChildCleanup : IAsyncCleanup
        {
            private readonly Scope child;
            private bool attempted;
            public ChildCleanup(Scope child) { this.child=child; }
            public string Id => "scope/"+child.Id;
            public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
            {
                var report=attempted ? await child.RetryFailedAsync(cleanup) : await child.CloseAsync(cleanup);
                attempted=true;
                // The parent ticket owns the child until actual drain, even if its own waiter expired.
                while(report.PendingCount>0) { await UniTask.Yield();report=child.Snapshot; }
                return report.IsFullyDrained ? Result.Success() : CleanupWait.Error(ErrorKind.CleanupFailed,"scope.child.cleanup.failed");
            }
        }
        private sealed class SubscriptionCleanup : IAsyncCleanup
        {
            public string Id { get; }
            internal IDisposable Subscription { get; }
            public SubscriptionCleanup(string id,IDisposable subscription) { Id=id;Subscription=subscription; }
            public UniTask<Result> CleanupAsync(CleanupContext cleanup) { Subscription.Dispose();return UniTask.FromResult(Result.Success()); }
        }
        private sealed class TaskCleanup : IAsyncCleanup
        {
            private readonly Scope owner;
            private readonly UniTaskCompletionSource<Result> completed=new UniTaskCompletionSource<Result>();
            private UniTask driver;
            public string Id { get; }
            public TaskCleanup(string id,Scope owner) { Id=id;this.owner=owner; }
            public void Start(Func<CancellationToken,UniTask<Result>> factory,CancellationToken cancellation) { driver=RunAsync(factory,cancellation).Preserve(); }
            private async UniTask RunAsync(Func<CancellationToken,UniTask<Result>> factory,CancellationToken cancellation)
            {
                Result result;
                try { result=await factory(cancellation); }
                catch(OperationCanceledException error) { result=CleanupWait.Error(cancellation.IsCancellationRequested && error.CancellationToken==cancellation ? ErrorKind.Canceled : ErrorKind.Unexpected,"scope.task.canceled.exception"); }
                catch(Exception error) { result=Result.Failure(new FrameworkError(ErrorKind.Unexpected,"scope.task.exception",error.GetType().Name)); }
                await UniTask.SwitchToMainThread();
                if(!result.IsSuccess) owner.taskDiagnostics.Add(result.Error);
                // Observing the task's terminal failure is successful cleanup, not permission to replay business work.
                owner.pendingTasks--;
                completed.TrySetResult(Result.Success());
            }
            public UniTask<Result> CleanupAsync(CleanupContext cleanup) => completed.Task;
        }
    }
}
