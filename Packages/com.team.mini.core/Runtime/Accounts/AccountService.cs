using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Ownership;
namespace MiniFramework.Accounts
{
    public sealed class AccountLimits
    {
        public int MaxOperations {get;}public int MaxSessions {get;}
        public AccountLimits(int maxOperations=8,int maxSessions=32){if(maxOperations<1||maxOperations>128||maxSessions<1||maxSessions>1024)throw new ArgumentOutOfRangeException();MaxOperations=maxOperations;MaxSessions=maxSessions;}
    }
    public sealed class AccountService:IAccountService,IModule,IAsyncCleanup,IDiagnosticSource
    {
        readonly OwnershipPolicy policy;readonly IOwnerGenerationSource generations;readonly IIdentityProvider provider;readonly string issuer;readonly IdentityMode identityMode;readonly AccountLimits limits;readonly CleanupContext release;
        readonly List<Switch> operations=new List<Switch>();readonly List<Session> sessions=new List<Session>();IScope root;Switch active;Session current;bool initialized,stopped,cleanupEntered;long lastGeneration;
        UniTask retryDriver;UniTaskCompletionSource<Result> retryFlight;
        public string Id {get;}public IReadOnlyList<string> Dependencies {get;}
        public string ContextGeneration {get;}=Guid.NewGuid().ToString("N");public ClaimOwnerMode Mode=>ClaimOwnerMode.AccountScoped;public AccountState State {get;private set;}=AccountState.Created;
        public IAccountSession Current{get{Main();return current!=null&&current.IsAccepting?current:null;}}
        public ClaimOwnerSnapshot? CurrentOwner=>Current?.Owner;public int PendingCount=>operations.Count;public int SessionCount=>sessions.Count;
        public int RetiringCount=>sessions.Count(s=>s.Retired&&s.Scope.State!=ScopeState.Closed);
        public AccountService(OwnershipPolicy policy,IOwnerGenerationSource generations,IIdentityProvider provider,AccountLimits limits=null,string id="account",string providerModuleId=null,CleanupContext? releasePolicy=null)
        {
            Main();if(policy==null||policy.Mode!=ClaimOwnerMode.AccountScoped||generations==null||provider==null||!PlatformIdentifier.IsValid(provider.Id)||!Enum.IsDefined(typeof(IdentityMode),provider.Mode)||string.IsNullOrWhiteSpace(id)||providerModuleId!=null&&string.IsNullOrWhiteSpace(providerModuleId))throw new ArgumentException();
            this.policy=policy;this.generations=generations;this.provider=provider;issuer=provider.Id;identityMode=provider.Mode;this.limits=limits??new AccountLimits();Id=id;Dependencies=providerModuleId==null?Array.Empty<string>():new[]{providerModuleId};release=releasePolicy??new CleanupContext(default,Timeout.InfiniteTimeSpan);
        }
        static void Main(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("Account service requires main thread.");}
        static FrameworkError Error(string code,ErrorKind kind=ErrorKind.Provider)=>new FrameworkError(kind,code);
        static FrameworkError Clean(FrameworkError e)=>new FrameworkError(e.Kind,e.Code,"",e.Retry);
        static FrameworkError? Interrupt(OperationContext wait,long start)
        {if(wait.Cancellation.IsCancellationRequested)return Error("account.wait.canceled",ErrorKind.Canceled);if(wait.Timeout!=Timeout.InfiniteTimeSpan&&(Stopwatch.GetTimestamp()-start)/(double)Stopwatch.Frequency>=wait.Timeout.TotalSeconds)return Error("account.wait.timeout",ErrorKind.Timeout);return null;}
        FrameworkError? Gate()
        {if(!initialized||stopped||root==null||root.State!=ScopeState.Open)return Error("account.not_ready",ErrorKind.NotReady);if(identityMode==IdentityMode.Simulated&&!policy.AllowSimulatedIdentity)return Error("account.simulated.disabled",ErrorKind.PermissionDenied);return null;}
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            Main();var e=Interrupt(operation,Stopwatch.GetTimestamp());if(e.HasValue)return UniTask.FromResult(Result.Failure(e.Value));if(initialized||stopped)return UniTask.FromResult(Result.Failure(Error("account.initialize.state",ErrorKind.InvalidState)));
            var registered=context.Cleanup.Register(this);if(!registered.IsSuccess)return UniTask.FromResult(Result.Failure(registered.Error));initialized=true;
            root=(context as IScopedModuleContext)?.Scope;if(root==null)return UniTask.FromResult(Result.Failure(Error("account.scope.required",ErrorKind.NotSupported)));
            if(identityMode==IdentityMode.Simulated&&!policy.AllowSimulatedIdentity)return UniTask.FromResult(Result.Failure(Error("account.simulated.disabled",ErrorKind.PermissionDenied)));
            State=AccountState.SignedOut;return UniTask.FromResult(Result.Success());
        }
        public Result ValidateCurrent(ClaimOwnerSnapshot owner)
        {Main();return CurrentOwner.HasValue&&owner.SameAs(CurrentOwner.Value)?Result.Success():Result.Failure(Error("account.current.mismatch",ErrorKind.NotReady));}
        public Result<StorageAddress> AddressForCurrent(string slot)
        {Main();if(!CurrentOwner.HasValue)return Result<StorageAddress>.Failure(Error("account.signed_out",ErrorKind.NotReady));if(!StorageIdentifier.IsValid(slot))return Result<StorageAddress>.Failure(Error("account.slot.invalid",ErrorKind.InvalidArgument));return Result<StorageAddress>.Success(new StorageAddress(CurrentOwner.Value.StorageNamespace,slot));}
        public Result<IAccountSwitch> CaptureSwitch()
        {
            Main();var e=Gate();if(e.HasValue)return Result<IAccountSwitch>.Failure(e.Value);
            if(operations.Count>=limits.MaxOperations)return Result<IAccountSwitch>.Failure(Error("account.capacity",ErrorKind.Unavailable));var op=new Switch(this);operations.Add(op);return Result<IAccountSwitch>.Success(op);
        }
        bool Check(Switch op){var e=stopped||root!=null&&root.State!=ScopeState.Open?Error("account.owner.closed",ErrorKind.Canceled):Interrupt(op.Wait,op.Start);if(e.HasValue)Cancel(op,e.Value);return op.Abandoned;}
        void Cancel(Switch op,FrameworkError error)
        {
            if(op.Done.Task.Status==UniTaskStatus.Pending){op.Abandoned=true;op.Done.TrySetResult(Result<IAccountSession>.Failure(error));}
            if(op.Ticket!=null&&!op.ActualEnded&&!op.CancelSent){op.CancelSent=true;try{var r=op.Ticket.Cancel();if(!r.IsSuccess)op.CancelError=Clean(r.Error);}catch(Exception){op.CancelError=Error("account.cancel.exception");}}
            if(!op.Started){operations.Remove(op);op.Ended.TrySetResult();}
        }
        void Finish(Switch op,Result<IAccountSession> result)=>op.Done.TrySetResult(result);
        void RetireCurrent()
        {
            var old=current;current=null;if(old==null)return;old.Accepting=false;old.Retired=true;
            // Clear public authority before Close triggers user cancellation callbacks.
            old.Driver=Retire(old).Preserve();
        }
        async UniTask Retire(Session session)
        {
            try{await session.Scope.CloseAsync(release);while(session.Scope.Cleanup.Snapshot.PendingCount>0)await UniTask.Yield();}
            finally{session.Ended.TrySetResult();}
        }
        async UniTask<Result> Release(Switch op,CleanupContext cleanup)
        {
            if(op.Ticket==null)return Result.Success();Result result;
            try{result=await op.Ticket.CleanupAsync(cleanup);await UniTask.SwitchToMainThread();}catch(Exception){await UniTask.SwitchToMainThread();result=Result.Failure(Error("account.cleanup.exception",ErrorKind.CleanupFailed));}
            if(!result.IsSuccess){op.CleanupError=Clean(result.Error);return Result.Failure(op.CleanupError.Value);}op.Ticket=null;op.CleanupError=null;return Result.Success();
        }
        Result<long> Reserve()
        {try{return generations.Reserve();}catch(Exception){return Result<long>.Failure(Error("account.generation.exception"));}}
        async UniTask Drive(Switch op)
        {
            FrameworkError? failure=null;BusinessIdentity identity=null;string accountNamespace=null;
            try
            {
                State=AccountState.Resolving;RetireCurrent();
                if(Check(op)){failure=Error("account.before_resolve.canceled",ErrorKind.Canceled);return;}
                op.Ticket=provider.Create();if(op.Ticket==null)throw new InvalidOperationException();
                if(Check(op)){op.ActualEnded=true;failure=Error("account.before_resolve.canceled",ErrorKind.Canceled);return;}
                Result<BusinessIdentity> result;
                try{result=await op.Ticket.ResolveAsync();await UniTask.SwitchToMainThread();}catch(Exception){await UniTask.SwitchToMainThread();result=Result<BusinessIdentity>.Failure(Error("account.resolve.exception"));}
                op.ActualEnded=true;
                if(!result.IsSuccess)failure=Clean(result.Error);
                else if(result.Value==null||result.Value.Issuer!=issuer||result.Value.Mode!=identityMode)failure=Error("account.identity.mismatch",ErrorKind.Integrity);
                else{var ns=policy.NamespaceFor(result.Value);if(!ns.IsSuccess)failure=ns.Error;else{identity=result.Value;accountNamespace=ns.Value;}}
            }
            catch(Exception){await UniTask.SwitchToMainThread();op.ActualEnded=true;failure=Error("account.factory.exception");}
            finally
            {
                var released=await Release(op,release);
                if(!released.IsSuccess){failure=failure??released.Error;if(!stopped)State=AccountState.PendingCleanup;}
                else
                {
                    if(!Check(op)&&!failure.HasValue&&identity!=null)
                    {
                        var generation=Reserve();
                        if(Check(op))failure=Error("account.before_publish.canceled",ErrorKind.Canceled);
                        else if(!generation.IsSuccess)failure=generation.Error;
                        else if(generation.Value<=lastGeneration)failure=Error("account.generation.invalid",ErrorKind.Integrity);
                        else
                        {
                            var child=root.CreateChild("account-"+generation.Value,ScopeKind.AccountScoped);
                            if(!child.IsSuccess)failure=child.Error;
                            else{lastGeneration=generation.Value;current=new Session(this,new ClaimOwnerSnapshot(Mode,identity.OwnerId,generation.Value,accountNamespace),child.Value);sessions.Add(current);}
                        }
                    }
                    if(!stopped)State=current==null?AccountState.SignedOut:AccountState.Ready;
                    operations.Remove(op);if(active==op)active=null;
                }
                if(op.Done.Task.Status==UniTaskStatus.Pending)Finish(op,failure.HasValue?Result<IAccountSession>.Failure(failure.Value):current==null?Result<IAccountSession>.Failure(Error("account.switch.abandoned",ErrorKind.Canceled)):Result<IAccountSession>.Success(current));
                op.DriverEnded=true;op.Ended.TrySetResult();
            }
        }
        async UniTask Retry(CleanupContext cleanup,UniTaskCompletionSource<Result> done,bool scopes)
        {
            var result=Result.Success();
            if(scopes)foreach(var session in sessions.Where(s=>s.Retired).ToArray())
            {
                await session.Ended.Task;
                if(session.Scope.State==ScopeState.CleanupFailed){var report=await session.Scope.RetryFailedAsync(cleanup);while(report.PendingCount>0){await UniTask.Yield();report=session.Scope.Cleanup.Snapshot;}if(!report.IsFullyDrained)result=Result.Failure(Error("account.retirement.failed",ErrorKind.CleanupFailed));}
            }
            foreach(var op in operations.ToArray())if(op.DriverEnded&&op.CleanupError.HasValue)
            {var r=await Release(op,cleanup);if(r.IsSuccess){operations.Remove(op);if(active==op)active=null;}else result=r;}
            if(!stopped&&active==null)State=current==null?AccountState.SignedOut:AccountState.Ready;done.TrySetResult(result);
        }
        public async UniTask<Result> RetryRetirementAsync(CleanupContext cleanup,OperationContext wait)
        {
            Main();var start=Stopwatch.GetTimestamp();var e=Interrupt(wait,start);if(e.HasValue)return Result.Failure(e.Value);
            if(retryFlight==null||retryFlight.Task.Status!=UniTaskStatus.Pending){retryFlight=new UniTaskCompletionSource<Result>();retryDriver=Retry(cleanup,retryFlight,true).Preserve();}
            var flight=retryFlight;while(true){e=Interrupt(wait,start);if(e.HasValue)return Result.Failure(e.Value);if(flight.Task.Status!=UniTaskStatus.Pending)return await flight.Task;await UniTask.Yield();}
        }
        sealed class Session:IAccountSession
        {
            readonly AccountService owner;internal bool Accepting=true,Retired;internal UniTask Driver;internal readonly UniTaskCompletionSource Ended=new UniTaskCompletionSource();
            public string ContextGeneration=>owner.ContextGeneration;public ClaimOwnerSnapshot Owner {get;}public IScope Scope {get;}
            public bool IsAccepting=>Accepting&&!owner.stopped&&ReferenceEquals(owner.current,this)&&Scope.State==ScopeState.Open&&owner.root.State==ScopeState.Open;
            internal Session(AccountService owner,ClaimOwnerSnapshot snapshot,IScope scope){this.owner=owner;Owner=snapshot;Scope=scope;}
        }
        sealed class Switch:IAccountSwitch
        {
            readonly AccountService owner;internal readonly UniTaskCompletionSource<Result<IAccountSession>> Done=new UniTaskCompletionSource<Result<IAccountSession>>();internal readonly UniTaskCompletionSource Ended=new UniTaskCompletionSource();
            internal OperationContext Wait;internal long Start;internal bool Started,Abandoned,ActualEnded,CancelSent,DriverEnded;internal IIdentityAttempt Ticket;internal UniTask Driver;
            public string AttemptId {get;}=Guid.NewGuid().ToString("N");public FrameworkError? CancelError {get;internal set;}public FrameworkError? CleanupError {get;internal set;}
            internal Switch(AccountService owner){this.owner=owner;}
            public async UniTask<Result<IAccountSession>> WaitAsync(OperationContext wait)
            {
                Main();if(Started)return Result<IAccountSession>.Failure(Error("account.wait.single_use",ErrorKind.InvalidState));Started=true;Wait=wait;Start=Stopwatch.GetTimestamp();
                if(owner.Check(this)){owner.operations.Remove(this);Ended.TrySetResult();return await Done.Task;}
                var e=owner.Gate();if(!e.HasValue&&owner.active!=null)e=Error("account.switch.busy",ErrorKind.Unavailable);if(!e.HasValue&&owner.sessions.Count>=owner.limits.MaxSessions)e=Error("account.sessions.capacity",ErrorKind.Unavailable);
                if(e.HasValue){owner.Finish(this,Result<IAccountSession>.Failure(e.Value));owner.operations.Remove(this);Ended.TrySetResult();return await Done.Task;}
                owner.active=this;Driver=owner.Drive(this).Preserve();while(true){owner.Check(this);if(Done.Task.Status!=UniTaskStatus.Pending)return await Done.Task;await UniTask.Yield();}
            }
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup)
        {Main();if(State==AccountState.Closed)return UniTask.FromResult(Result.Success());stopped=true;State=AccountState.Closing;RetireCurrent();foreach(var op in operations.ToArray())Cancel(op,Error("account.owner.closed",ErrorKind.Canceled));return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            Main();var retry=cleanupEntered;cleanupEntered=true;await StopAsync(cleanup);foreach(var op in operations.ToArray())await op.Ended.Task;
            foreach(var session in sessions.Where(s=>s.Retired).ToArray())await session.Ended.Task;
            if(retryFlight!=null&&retryFlight.Task.Status==UniTaskStatus.Pending)await retryFlight.Task;
            // Parent Scope owns child cleanup retries; do not retry the same child a second time here.
            if(retry){var done=new UniTaskCompletionSource<Result>();await Retry(cleanup,done,false);}
            if(operations.Count>0||RetiringCount>0)return Result.Failure(Error("account.cleanup.retained",ErrorKind.CleanupFailed));State=AccountState.Closed;return Result.Success();
        }
        public DiagnosticCounters ReadDiagnostics(){Main();return new DiagnosticCounters{PendingOperations=operations.Count+sessions.Count(s=>s.Retired&&s.Scope.State==ScopeState.Closing),ActiveScopes=sessions.Count(s=>s.Scope.State!=ScopeState.Closed),FailedCleanup=operations.Count(o=>o.CleanupError.HasValue)+sessions.Count(s=>s.Scope.State==ScopeState.CleanupFailed)};}
    }
}
