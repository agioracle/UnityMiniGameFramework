using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Ads
{
    public sealed class AdsLimits
    {
        public int MaxOperations {get;} public int QueueCapacity {get;} public int CallbacksPerFrame {get;} public int MaxClaims {get;}
        public AdsLimits(int maxOperations=16,int queueCapacity=256,int callbacksPerFrame=32,int maxClaims=128)
        {
            if(maxOperations<1||maxOperations>1024||queueCapacity<1||queueCapacity>4096||callbacksPerFrame<1||callbacksPerFrame>queueCapacity||maxClaims<1||maxClaims>65536)throw new ArgumentOutOfRangeException();
            MaxOperations=maxOperations;QueueCapacity=queueCapacity;CallbacksPerFrame=callbacksPerFrame;MaxClaims=maxClaims;
        }
    }
    // Coordination owns native work until actual termination, fact retention and release are confirmed.
    public sealed class AdsService:IAdsService,IModule,IAsyncCleanup,IDiagnosticSource,IAdCallbackSink
    {
        readonly IRewardedAdBackend backend; readonly PlatformDescriptor descriptor; readonly IAdReceiptJournal journal; readonly AdsLimits limits; readonly CleanupContext releasePolicy;
        readonly List<Operation> operations=new List<Operation>(); readonly List<Operation> queue=new List<Operation>();
        readonly Dictionary<string,ClaimOwnerSnapshot> claims=new Dictionary<string,ClaimOwnerSnapshot>(StringComparer.Ordinal);
        readonly object gate=new object(); readonly Queue<KeyValuePair<long,AdCallback>> inbox=new Queue<KeyValuePair<long,AdCallback>>();
        readonly UniTaskCompletionSource pumpEnded=new UniTaskCompletionSource();
        IPlatformCapabilities platform; IPlatformLifecycle lifecycle; IClaimOwnerAuthority ownerAuthority; readonly bool requireOwner; Operation active;
        bool initialized,ready,stopped,accepting,pumping,cleanupEntered; long accepted,processed,rejected; int discarded;
        UniTask pumpDriver,retryDriver; UniTaskCompletionSource<Result> retryFlight;
        public string Id {get;} public IReadOnlyList<string> Dependencies {get;}
        public string Generation {get;}=Guid.NewGuid().ToString("N"); public bool IsClosed {get;private set;}
        public int PendingCount=>operations.Count; public int ActiveAttemptCount=>active==null?0:1;
        public int QueuedCount {get{lock(gate)return inbox.Count;}} public long RejectedCallbacks=>Interlocked.Read(ref rejected); public int DiscardedCallbacks=>discarded;
        public AdsService(IRewardedAdBackend backend,IAdReceiptJournal journal,AdsLimits limits=null,string id="ads",string platformModuleId="platform",CleanupContext? releasePolicy=null,string ownerModuleId=null)
        {
            Main();this.backend=backend??throw new ArgumentNullException(nameof(backend));this.journal=journal??throw new ArgumentNullException(nameof(journal));this.limits=limits??new AdsLimits();
            if(backend.Descriptor==null||string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(platformModuleId)||ownerModuleId!=null&&string.IsNullOrWhiteSpace(ownerModuleId))throw new ArgumentException();
            descriptor=backend.Descriptor;Id=id;requireOwner=ownerModuleId!=null;Dependencies=requireOwner?new[]{platformModuleId,ownerModuleId}:new[]{platformModuleId};this.releasePolicy=releasePolicy??new CleanupContext(default,Timeout.InfiniteTimeSpan);
        }
        static void Main(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("Ads service requires main thread.");}
        static FrameworkError Error(string code,ErrorKind kind=ErrorKind.Provider)=>new FrameworkError(kind,code);
        static FrameworkError Clean(FrameworkError e)=>new FrameworkError(e.Kind,e.Code,"",e.Retry);
        static FrameworkError? Interrupted(OperationContext wait,long start)
        {
            if(wait.Cancellation.IsCancellationRequested)return Error("ads.wait.canceled",ErrorKind.Canceled);
            if(wait.Timeout!=Timeout.InfiniteTimeSpan&&(Stopwatch.GetTimestamp()-start)/(double)Stopwatch.Frequency>=wait.Timeout.TotalSeconds)return Error("ads.wait.timeout",ErrorKind.Timeout);
            return null;
        }
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            Main();var error=Interrupted(operation,Stopwatch.GetTimestamp());
            if(error.HasValue)return UniTask.FromResult(Result.Failure(error.Value));
            if(initialized||stopped)return UniTask.FromResult(Result.Failure(Error("ads.initialize.state",ErrorKind.InvalidState)));
            var registered=context.Cleanup.Register(this);if(!registered.IsSuccess)return UniTask.FromResult(Result.Failure(registered.Error));initialized=true;
            if(!context.Services.TryGet(out platform)||!context.Services.TryGet(out lifecycle))return UniTask.FromResult(Result.Failure(Error("ads.platform.missing",ErrorKind.Dependency)));
            if(platform.Descriptor.Id!=descriptor.Id||platform.Descriptor.Mode!=descriptor.Mode||backend.Descriptor.Id!=descriptor.Id||backend.Descriptor.Mode!=descriptor.Mode)return UniTask.FromResult(Result.Failure(Error("ads.platform.mismatch",ErrorKind.Integrity)));
            if(requireOwner&&!context.Services.TryGet(out ownerAuthority))return UniTask.FromResult(Result.Failure(Error("ads.owner.missing",ErrorKind.Dependency)));
            ready=true;lock(gate)accepting=true;pumping=true;pumpDriver=Pump().Preserve();return UniTask.FromResult(Result.Success());
        }
        FrameworkError? Gate(RewardedAdRequest request)
        {
            if(!ready||stopped||lifecycle.State!=PlatformState.Ready)return Error("ads.not_ready",ErrorKind.NotReady);
            if(requireOwner){var valid=ownerAuthority.ValidateCurrent(request.Owner);if(!valid.IsSuccess)return Clean(valid.Error);}
            else if(request.Owner.Mode!=ClaimOwnerMode.LocalOnly)return Error("ads.owner.mode_pending",ErrorKind.NotSupported);
            var cap=platform.Capabilities.Get(PlatformFeature.RewardedAds);
            if(cap.Support==CapabilitySupport.Unsupported)return Error("ads.unsupported",ErrorKind.NotSupported);
            if(cap.Availability==CapabilityAvailability.PermissionDenied)return Error("ads.permission",ErrorKind.PermissionDenied);
            if(!cap.IsUsable)return Error("ads.unavailable",ErrorKind.Unavailable);
            if((cap.Constraints&CapabilityConstraints.ForegroundOnly)!=0&&lifecycle.Visibility!=PlatformVisibility.Foreground)return Error("ads.foreground",ErrorKind.Unavailable);
            if((cap.Constraints&CapabilityConstraints.UserGestureRequired)!=0&&!request.UserInitiated)return Error("ads.gesture",ErrorKind.InvalidArgument);
            return null;
        }
        public Result<IAdOperation> Capture(RewardedAdRequest request)
        {
            Main();if(request==null)return Result<IAdOperation>.Failure(Error("ads.request.null",ErrorKind.InvalidArgument));
            var e=Gate(request);if(e.HasValue)return Result<IAdOperation>.Failure(e.Value);
            if(operations.Count>=limits.MaxOperations)return Result<IAdOperation>.Failure(Error("ads.capacity",ErrorKind.Unavailable));
            var op=new Operation(this,request);operations.Add(op);return Result<IAdOperation>.Success(op);
        }
        bool Check(Operation op)
        {
            var e=stopped?Error("ads.owner.closed",ErrorKind.Canceled):Interrupted(op.Wait,op.Start);
            if(!e.HasValue&&requireOwner&&!ownerAuthority.ValidateCurrent(op.Request.Owner).IsSuccess)e=Error("ads.owner.changed",ErrorKind.Canceled);
            if(e.HasValue)Cancel(op,e.Value);return op.Abandoned;
        }
        void Cancel(Operation op,FrameworkError e)
        {
            if(op.Done.Task.Status==UniTaskStatus.Pending)
            {op.Abandoned=true;op.ObserverState=e.Kind==ErrorKind.Timeout?AdObserverState.TimedOut:AdObserverState.Canceled;op.Done.TrySetResult(Result<AdPresentationReceipt>.Failure(e));}
            if(op.Ticket!=null&&op.LoadStarted&&!op.LoadEnded&&!op.ShowInvoked&&!op.CancelSent)
            {
                op.CancelSent=true;try{var r=op.Ticket.CancelLoad();if(!r.IsSuccess)op.CancelError=Clean(r.Error);}catch(Exception){op.CancelError=Error("ads.cancel.exception");}
            }
            if(!op.WaitStarted){operations.Remove(op);op.Phase=AdPhase.NotShown;op.Ended.TrySetResult();}
        }
        void Finish(Operation op,Result<AdPresentationReceipt> result)
        {if(op.Done.Task.Status!=UniTaskStatus.Pending)return;op.ObserverState=result.IsSuccess?AdObserverState.Succeeded:AdObserverState.Failed;op.Done.TrySetResult(result);}
        public bool TryPost(AdCallback callback)
        {
            if(callback==null)return false;lock(gate)
            {if(!accepting||inbox.Count>=limits.QueueCapacity){Interlocked.Increment(ref rejected);return false;}inbox.Enqueue(new KeyValuePair<long,AdCallback>(++accepted,callback));return true;}
        }
        async UniTask Pump()
        {
            try{while(pumping){Drain(limits.CallbacksPerFrame);await UniTask.Yield();}}
            finally{lock(gate){processed=accepted;inbox.Clear();}pumpEnded.TrySetResult();}
        }
        void Drain(int max)
        {
            var count=Math.Min(max,QueuedCount);for(var i=0;i<count;i++)
            {
                KeyValuePair<long,AdCallback> item;lock(gate)item=inbox.Dequeue();var cb=item.Value;var op=active;
                if(op==null||!op.ShowInvoked||op.Sealed||cb.Generation!=Generation||cb.AttemptId!=op.AttemptId||cb.Sequence<=op.LastSequence||op.ShowEnded&&item.Key>op.Fence){discarded++;}
                else
                {
                    op.LastSequence=cb.Sequence;
                    if(!op.StartedCallback&&(cb.Kind==AdCallbackKind.Completed||cb.Kind==AdCallbackKind.Closed))op.CallbackError=Error("ads.callback.order",ErrorKind.Integrity);
                    switch(cb.Kind){case AdCallbackKind.Started:op.StartedCallback=true;break;case AdCallbackKind.Completed:op.CompletedCallback=true;break;case AdCallbackKind.Closed:op.ClosedCallback=true;break;case AdCallbackKind.Failed:op.CallbackError=Clean(cb.Error.Value);break;}
                }
                processed=item.Key;
            }
        }
        async UniTask Drive(Operation op)
        {
            FrameworkError? error=null;var outcome=AdWatchOutcome.NotShown;
            try
            {
                while(active!=null||queue.Count==0||queue[0]!=op){if(Check(op))return;await UniTask.Yield();}
                if(Check(op))return;queue.Remove(op);active=op;
                error=Gate(op.Request);if(error.HasValue)return;
                op.Ticket=backend.Create(Generation,op.AttemptId,op.Request.PlacementId,this);if(op.Ticket==null)throw new InvalidOperationException();
                if(Check(op)){error=Error("ads.before_load.canceled",ErrorKind.Canceled);return;}
                op.Phase=AdPhase.Loading;op.LoadStarted=true;Result load;
                try{load=await op.Ticket.LoadAsync();await UniTask.SwitchToMainThread();}catch(Exception){await UniTask.SwitchToMainThread();load=Result.Failure(Error("ads.load.exception"));}
                op.LoadEnded=true;
                if(!load.IsSuccess){error=Clean(load.Error);outcome=AdWatchOutcome.Failed;return;}
                if(Check(op)){error=Error("ads.before_show.canceled",ErrorKind.Canceled);return;}
                op.Phase=AdPhase.Ready;error=Gate(op.Request);if(error.HasValue)return;
                // Once invoked, cancellation cannot release the native presentation slot.
                op.ShowInvoked=true;op.Phase=AdPhase.Showing;Result show;
                try{show=await op.Ticket.ShowAsync();await UniTask.SwitchToMainThread();}catch(Exception){await UniTask.SwitchToMainThread();show=Result.Failure(Error("ads.show.exception"));}
                op.ShowEnded=true;lock(gate)op.Fence=accepted;
                while(processed<op.Fence){Check(op);await UniTask.Yield();}
                op.Sealed=true;
                error=!show.IsSuccess?(FrameworkError?)Clean(show.Error):op.CallbackError;
                if(!error.HasValue&&(!op.StartedCallback||!op.ClosedCallback))error=Error("ads.callback.incomplete",ErrorKind.Integrity);
                outcome=error.HasValue?AdWatchOutcome.Failed:op.CompletedCallback?AdWatchOutcome.Completed:AdWatchOutcome.Skipped;
            }
            catch(Exception){await UniTask.SwitchToMainThread();error=Error("ads.factory.exception");outcome=AdWatchOutcome.Failed;}
            finally
            {
                queue.Remove(op);
                if(active==op)
                {
                    op.Sealed=true;op.Receipt=new AdPresentationReceipt(op.AttemptId,op.Request,descriptor.Mode,outcome,op.ShowInvoked,error);
                    await Finalize(op,releasePolicy);
                    Check(op); // Recheck observation at publication, including same-frame owner changes.
                    if(op.Done.Task.Status==UniTaskStatus.Pending)
                    {
                        var failed=error??op.FinalizationError;
                        Finish(op,failed.HasValue?Result<AdPresentationReceipt>.Failure(failed.Value):Result<AdPresentationReceipt>.Success(op.Receipt));
                    }
                }
                else {operations.Remove(op);op.Phase=AdPhase.NotShown;}
                op.DriverEnded=true;op.Ended.TrySetResult();
            }
        }
        async UniTask<Result> Finalize(Operation op,CleanupContext cleanup)
        {
            op.Phase=AdPhase.Draining;Result result;
            if(!op.Recorded)
            {
                try{result=journal.Record(op.Receipt);}catch(Exception){result=Result.Failure(Error("ads.receipt.exception",ErrorKind.CleanupFailed));}
                if(!result.IsSuccess)return Retain(op,result.Error);op.Recorded=true;
            }
            if(op.Ticket!=null)
            {
                try{result=await op.Ticket.CleanupAsync(cleanup);await UniTask.SwitchToMainThread();}catch(Exception){await UniTask.SwitchToMainThread();result=Result.Failure(Error("ads.cleanup.exception",ErrorKind.CleanupFailed));}
                if(!result.IsSuccess)return Retain(op,result.Error);op.Ticket=null;
            }
            op.FinalizationError=null;op.Phase=op.Receipt.Outcome==AdWatchOutcome.Completed?AdPhase.Completed:op.Receipt.Outcome==AdWatchOutcome.Skipped?AdPhase.Skipped:op.Receipt.Outcome==AdWatchOutcome.NotShown?AdPhase.NotShown:AdPhase.Failed;
            operations.Remove(op);if(active==op)active=null;return Result.Success();
        }
        static Result Retain(Operation op,FrameworkError error)
        {op.FinalizationError=Clean(error);op.Phase=AdPhase.PendingFinalization;return Result.Failure(op.FinalizationError.Value);}
        async UniTask Retry(CleanupContext cleanup,UniTaskCompletionSource<Result> done)
        {
            var result=Result.Success();foreach(var op in operations.ToArray())if(op.DriverEnded&&op.Phase==AdPhase.PendingFinalization){var r=await Finalize(op,cleanup);if(!r.IsSuccess)result=r;}
            done.TrySetResult(result);
        }
        public async UniTask<Result> RetryFinalizationAsync(CleanupContext cleanup,OperationContext wait)
        {
            Main();var start=Stopwatch.GetTimestamp();var e=Interrupted(wait,start);if(e.HasValue)return Result.Failure(e.Value);
            if(retryFlight==null||retryFlight.Task.Status!=UniTaskStatus.Pending)
            {
                if(!operations.Any(o=>o.DriverEnded&&o.Phase==AdPhase.PendingFinalization))return Result.Failure(Error("ads.retry.no_retained",ErrorKind.InvalidState));
                retryFlight=new UniTaskCompletionSource<Result>();retryDriver=Retry(cleanup,retryFlight).Preserve();
            }
            var flight=retryFlight;while(true){e=Interrupted(wait,start);if(e.HasValue)return Result.Failure(e.Value);if(flight.Task.Status!=UniTaskStatus.Pending)return await flight.Task;await UniTask.Yield();}
        }
        sealed class Operation:IAdOperation
        {
            readonly AdsService owner;internal readonly UniTaskCompletionSource<Result<AdPresentationReceipt>> Done=new UniTaskCompletionSource<Result<AdPresentationReceipt>>();internal readonly UniTaskCompletionSource Ended=new UniTaskCompletionSource();
            internal OperationContext Wait;internal long Start,LastSequence,Fence;internal bool WaitStarted,Abandoned,LoadStarted,LoadEnded,ShowInvoked,ShowEnded,CancelSent,Sealed,StartedCallback,CompletedCallback,ClosedCallback,Recorded,DriverEnded;
            internal FrameworkError? CallbackError;internal IRewardedAdAttempt Ticket;internal UniTask Driver;
            public string AttemptId {get;}=Guid.NewGuid().ToString("N");public RewardedAdRequest Request {get;}public AdPhase Phase {get;internal set;}=AdPhase.Captured;public AdObserverState ObserverState {get;internal set;}=AdObserverState.Created;
            public AdPresentationReceipt Receipt {get;internal set;}public FrameworkError? FinalizationError {get;internal set;}public FrameworkError? CancelError {get;internal set;}
            internal Operation(AdsService owner,RewardedAdRequest request){this.owner=owner;Request=request;}
            public async UniTask<Result<AdPresentationReceipt>> WaitAsync(OperationContext wait)
            {
                Main();if(WaitStarted)return Result<AdPresentationReceipt>.Failure(Error("ads.wait.single_use",ErrorKind.InvalidState));WaitStarted=true;Wait=wait;Start=Stopwatch.GetTimestamp();
                if(owner.Check(this)){Phase=AdPhase.NotShown;owner.operations.Remove(this);Ended.TrySetResult();return await Done.Task;}
                var e=owner.Gate(Request);
                if(!e.HasValue&&owner.claims.TryGetValue(Request.ClaimId,out var bound))e=Error(bound.SameAs(Request.Owner)?"ads.claim.duplicate":"ads.claim.owner_conflict",bound.SameAs(Request.Owner)?ErrorKind.InvalidState:ErrorKind.Integrity);
                if(!e.HasValue&&owner.claims.Count>=owner.limits.MaxClaims)e=Error("ads.claim.capacity",ErrorKind.Unavailable);
                if(e.HasValue){owner.Finish(this,Result<AdPresentationReceipt>.Failure(e.Value));owner.operations.Remove(this);Phase=AdPhase.NotShown;Ended.TrySetResult();return await Done.Task;}
                owner.claims.Add(Request.ClaimId,Request.Owner);Phase=AdPhase.Queued;ObserverState=AdObserverState.Waiting;owner.queue.Add(this);Driver=owner.Drive(this).Preserve();
                while(true){owner.Check(this);if(Done.Task.Status!=UniTaskStatus.Pending)return await Done.Task;await UniTask.Yield();}
            }
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup)
        {
            Main();stopped=true;ready=false;foreach(var op in operations.ToArray())Cancel(op,Error("ads.owner.closed",ErrorKind.Canceled));return UniTask.FromResult(Result.Success());
        }
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            Main();var retry=cleanupEntered;cleanupEntered=true;await StopAsync(cleanup);
            foreach(var op in operations.ToArray())await op.Ended.Task;
            if(retryFlight!=null&&retryFlight.Task.Status==UniTaskStatus.Pending)await retryFlight.Task;
            if(retry){var done=new UniTaskCompletionSource<Result>();await Retry(cleanup,done);}
            lock(gate)accepting=false;pumping=false;if(pumpDriver.Status==UniTaskStatus.Pending)await pumpEnded.Task;
            if(operations.Count!=0)return Result.Failure(Error("ads.cleanup.retained",ErrorKind.CleanupFailed));
            IsClosed=true;return Result.Success();
        }
        public DiagnosticCounters ReadDiagnostics(){Main();return new DiagnosticCounters{PendingOperations=PendingCount,FailedCleanup=operations.Count(o=>o.Phase==AdPhase.PendingFinalization),QueuedCallbacks=QueuedCount};}
    }
}
