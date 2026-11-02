using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    public sealed class CleanupRegistry : IRetryableCleanupRegistry
    {
        private readonly List<Ticket> tickets = new List<Ticket>();
        private readonly Dictionary<string, Ticket> byId = new Dictionary<string, Ticket>(StringComparer.Ordinal);
        private bool driving;
        private UniTask driver;
        public CleanupRegistryState State { get; private set; }
        public CleanupRegistry() { CleanupWait.MainThread(); }
        public CleanupReport Snapshot
        {
            get
            {
                CleanupWait.MainThread();
                var pending=0; var failed=0;
                foreach (var ticket in tickets)
                {
                    if (ticket.State == CleanupState.Registered || ticket.State == CleanupState.Running) pending++;
                    if (ticket.State == CleanupState.Failed) failed++;
                }
                var drained=State != CleanupRegistryState.Open && pending==0 && failed==0;
                if (drained) State=CleanupRegistryState.Closed;
                return new CleanupReport(pending,failed,drained);
            }
        }
        public Result<ICleanupTicket> Register(IAsyncCleanup cleanup)
        {
            CleanupWait.MainThread();
            if (State != CleanupRegistryState.Open) return Reject(ErrorKind.InvalidState,"cleanup.registry.closed");
            if(cleanup==null) return Reject(ErrorKind.InvalidArgument,"cleanup.id.required");
            var id=cleanup.Id;
            if(string.IsNullOrWhiteSpace(id)) return Reject(ErrorKind.InvalidArgument,"cleanup.id.required");
            if (byId.TryGetValue(id,out var previous))
                return ReferenceEquals(previous.Owned,cleanup) ? Result<ICleanupTicket>.Success(previous) : Reject(ErrorKind.InvalidArgument,"cleanup.id.duplicate");
            // One object cannot acquire a second ticket by changing its Id.
            foreach(var existing in tickets)
                if(ReferenceEquals(existing.Owned,cleanup)) return Reject(ErrorKind.InvalidArgument,"cleanup.id.changed");
            var ticket=new Ticket(this,id,cleanup); byId.Add(id,ticket); tickets.Add(ticket);
            return Result<ICleanupTicket>.Success(ticket);
        }
        private static Result<ICleanupTicket> Reject(ErrorKind kind,string code) => Result<ICleanupTicket>.Failure(new FrameworkError(kind,code));
        internal void Seal()
        {
            CleanupWait.MainThread();
            if(State==CleanupRegistryState.Open) State=CleanupRegistryState.Closing;
        }
        public UniTask<CleanupReport> CloseAsync(CleanupContext cleanup)
        {
            CleanupWait.MainThread();
            if(State==CleanupRegistryState.Open) State=CleanupRegistryState.Closing;
            StartDrain(cleanup,false);
            return ObserveAsync(cleanup);
        }
        public UniTask<CleanupReport> RetryFailedAsync(CleanupContext cleanup)
        {
            CleanupWait.MainThread();
            // An Open registry has not attempted cleanup; retry must not implicitly close it.
            if(State!=CleanupRegistryState.Open) StartDrain(cleanup,true);
            return ObserveAsync(cleanup);
        }
        private void StartDrain(CleanupContext cleanup,bool retry)
        {
            if(driving || State==CleanupRegistryState.Closed) return;
            driving=true;
            driver=DrainAsync(cleanup,retry).Preserve();
        }
        private async UniTask DrainAsync(CleanupContext cleanup,bool retry)
        {
            try
            {
                for(var i=tickets.Count-1;i>=0;--i)
                {
                    var ticket=tickets[i];
                    if(ticket.State==CleanupState.Registered || (retry && ticket.State==CleanupState.Failed)) ticket.Start(cleanup);
                    // Wait for actual termination, never an individual waiter's deadline.
                    if(ticket.State==CleanupState.Running) await ticket.Actual;
                }
            }
            finally { driving=false; var ignored=Snapshot; }
        }
        private async UniTask<CleanupReport> ObserveAsync(CleanupContext cleanup)
        {
            var start=CleanupWait.Now; var wait=CleanupWait.Operation(cleanup);
            while(true)
            {
                var snapshot=Snapshot;
                if(CleanupWait.Interrupted(wait,start).HasValue || snapshot.PendingCount==0 || State==CleanupRegistryState.Open) return snapshot;
                await UniTask.Yield();
            }
        }
        private sealed class Ticket : ICleanupTicket
        {
            private readonly CleanupRegistry owner;
            internal IAsyncCleanup Owned;
            private UniTaskCompletionSource<Result> attempt=new UniTaskCompletionSource<Result>();
            private UniTask driver;
            public string Id { get; }
            public CleanupState State { get; private set; }
            public Result LastAttempt { get; private set; }
            internal UniTask<Result> Actual => attempt.Task;
            internal Ticket(CleanupRegistry owner,string id,IAsyncCleanup cleanup) { this.owner=owner;Id=id;Owned=cleanup; }
            internal void Start(CleanupContext cleanup)
            {
                if(State==CleanupState.Failed) attempt=new UniTaskCompletionSource<Result>();
                State=CleanupState.Running;
                driver=RunAsync(cleanup,attempt).Preserve();
            }
            private async UniTask RunAsync(CleanupContext cleanup,UniTaskCompletionSource<Result> completion)
            {
                Result result;
                try { result=await Owned.CleanupAsync(cleanup); }
                catch(OperationCanceledException error)
                {
                    result=CleanupWait.Error(cleanup.ShutdownCancellation.IsCancellationRequested && error.CancellationToken==cleanup.ShutdownCancellation ? ErrorKind.Canceled : ErrorKind.Unexpected,"cleanup.attempt.canceled.exception");
                }
                catch(Exception error) { result=Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"cleanup.attempt.exception",error.GetType().Name)); }
                await UniTask.SwitchToMainThread();
                LastAttempt=result; State=result.IsSuccess ? CleanupState.Succeeded : CleanupState.Failed;
                if(result.IsSuccess) Owned=null; // Do not keep released resource graphs alive through a ticket.
                completion.TrySetResult(result);
                var ignored=owner.Snapshot;
            }
            public UniTask<Result> RetryAsync(CleanupContext cleanup)
            {
                CleanupWait.MainThread();
                if(State==CleanupState.Registered) return UniTask.FromResult(CleanupWait.Error(ErrorKind.InvalidState,"cleanup.retry.not.started"));
                if(State==CleanupState.Failed) Start(cleanup);
                return WaitAsync(CleanupWait.Operation(cleanup));
            }
            public async UniTask<Result> WaitAsync(OperationContext wait)
            {
                CleanupWait.MainThread(); var start=CleanupWait.Now;
                // Capture this attempt. A later explicit retry must not change an existing waiter's result.
                UniTaskCompletionSource<Result> observed=attempt;
                while(true)
                {
                    var interrupted=CleanupWait.Interrupted(wait,start);
                    if(interrupted.HasValue) return interrupted.Value;
                    if(observed==null) observed=attempt;
                    if(observed!=null && observed.Task.Status!=UniTaskStatus.Pending) return await observed.Task;
                    await UniTask.Yield();
                }
            }
        }
    }
}
