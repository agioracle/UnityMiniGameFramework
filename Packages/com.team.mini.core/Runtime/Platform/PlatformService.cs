using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
namespace MiniFramework.Platform
{
    public sealed class PlatformLimits
    {
        public int MaxOperations {get;}public int QueueCapacity {get;}public int CallbacksPerFrame {get;}public int MaxCredentials {get;}
        public PlatformLimits(int maxOperations=16,int queueCapacity=256,int callbacksPerFrame=32,int maxCredentials=32)
        {if(maxOperations<1||maxOperations>1024||queueCapacity<1||queueCapacity>4096||callbacksPerFrame<1||callbacksPerFrame>queueCapacity||maxCredentials<1||maxCredentials>1024)throw new ArgumentOutOfRangeException();MaxOperations=maxOperations;QueueCapacity=queueCapacity;CallbacksPerFrame=callbacksPerFrame;MaxCredentials=maxCredentials;}
    }
    public sealed class PlatformService:IPlatformCapabilities,IPlatformLifecycle,IPlatformLogin,IPlatformCallbackSink,IModule,IAsyncCleanup,IDiagnosticSource
    {
        readonly IPlatformBackend backend;readonly PlatformLimits limits;readonly CleanupContext releasePolicy;
        readonly object gate=new object();readonly Queue<KeyValuePair<long,PlatformCallback>> inbox=new Queue<KeyValuePair<long,PlatformCallback>>();
        readonly EventChannel<PlatformLifecycleEvent> lifecycle=new EventChannel<PlatformLifecycleEvent>();readonly EventChannel<PlatformCapabilities> capabilityEvents=new EventChannel<PlatformCapabilities>();
        readonly List<Login> operations=new List<Login>();readonly List<PlatformLoginCredential> credentials=new List<PlatformLoginCredential>();
        IPlatformSession session;Login active;bool accepting,stopped,pumping,initialized,loadAbandoned,cleanupEntered,sessionFailed;
        long accepted,processed,lastSequence,rejected;int discarded,callbackErrors;PlatformCapabilities rawCapabilities;
        OperationContext loadWait;long loadStart;UniTaskCompletionSource<Result> loaded;UniTask loadDriver,pumpDriver;
        readonly UniTaskCompletionSource pumpEnded=new UniTaskCompletionSource();
        public string Id {get;}public IReadOnlyList<string> Dependencies {get;}
        public string Generation {get;}=Guid.NewGuid().ToString("N");public PlatformDescriptor Descriptor {get;}
        public PlatformState State {get;private set;}public PlatformVisibility Visibility {get;private set;}
        public PlatformCapabilities Capabilities {get;private set;}=new PlatformCapabilities(Array.Empty<PlatformCapability>());
        public int PendingCount=>operations.Count+(State==PlatformState.Initializing?1:0);public int ActiveAttemptCount=>active==null?0:1;
        public int QueuedCount {get{lock(gate)return inbox.Count;}}public long RejectedCallbacks=>Interlocked.Read(ref rejected);
        public int DiscardedCallbacks=>discarded;public int CallbackErrors=>callbackErrors;public int HeldCredentialCount=>credentials.Count(c=>!c.IsDisposed);
        public PlatformService(IPlatformBackend backend,PlatformLimits limits=null,string id="platform",string backendModuleId=null,CleanupContext? releasePolicy=null)
        {
            Main();this.backend=backend??throw new ArgumentNullException(nameof(backend));Descriptor=backend.Descriptor??throw new ArgumentException("Descriptor required.");this.limits=limits??new PlatformLimits();
            if(string.IsNullOrWhiteSpace(id)||backendModuleId!=null&&string.IsNullOrWhiteSpace(backendModuleId))throw new ArgumentException();Id=id;Dependencies=backendModuleId==null?Array.Empty<string>():new[]{backendModuleId};this.releasePolicy=releasePolicy??new CleanupContext(default,Timeout.InfiniteTimeSpan);
        }
        static void Main(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("Platform service requires main thread.");}
        static FrameworkError Error(string code,ErrorKind kind=ErrorKind.Provider)=>new FrameworkError(kind,code);
        static FrameworkError Clean(FrameworkError error)=>new FrameworkError(error.Kind,error.Code,"",error.Retry);
        static FrameworkError? Interrupt(OperationContext wait,long started)
        {if(wait.Cancellation.IsCancellationRequested)return Error("platform.wait.canceled",ErrorKind.Canceled);if(wait.Timeout!=Timeout.InfiniteTimeSpan&&(Stopwatch.GetTimestamp()-started)/(double)Stopwatch.Frequency>=wait.Timeout.TotalSeconds)return Error("platform.wait.timeout",ErrorKind.Timeout);return null;}
        public bool TryPost(PlatformCallback callback)
        {
            if(callback==null)return false;
            lock(gate){if(!accepting||inbox.Count>=limits.QueueCapacity){Interlocked.Increment(ref rejected);return false;}inbox.Enqueue(new KeyValuePair<long,PlatformCallback>(++accepted,callback));return true;}
        }
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            Main();var start=Stopwatch.GetTimestamp();var error=Interrupt(operation,start);if(error.HasValue)return UniTask.FromResult(Result.Failure(error.Value));
            if(initialized||stopped)return UniTask.FromResult(Result.Failure(Error("platform.initialize.state",ErrorKind.InvalidState)));
            var registered=context.Cleanup.Register(this);if(!registered.IsSuccess)return UniTask.FromResult(Result.Failure(registered.Error));
            initialized=true;loadWait=operation;loadStart=start;loaded=new UniTaskCompletionSource<Result>();State=PlatformState.Initializing;
            lock(gate)accepting=true;pumping=true;pumpDriver=Pump().Preserve();loadDriver=Load().Preserve();return ObserveLoad();
        }
        bool LoadInterrupted(){if(Interrupt(loadWait,loadStart).HasValue)loadAbandoned=true;return stopped||loadAbandoned;}
        async UniTask Load()
        {
            Result result;
            try
            {
                session=backend.Open(Generation,this);if(session==null)throw new InvalidOperationException();
                var ready=await session.InitializeAsync();await UniTask.SwitchToMainThread();
                if(!ready.IsSuccess)result=Result.Failure(Clean(ready.Error));
                else if(ready.Value==null)result=Result.Failure(Error("platform.initialize.null"));
                else if(LoadInterrupted())result=Result.Failure(Interrupt(loadWait,loadStart)??Error("platform.initialize.abandoned",ErrorKind.Canceled));
                else{rawCapabilities=ready.Value.Capabilities;Visibility=ready.Value.Visibility;lastSequence=ready.Value.Sequence;RecomputeCapabilities(false);result=Result.Success();}
            }
            catch(Exception){await UniTask.SwitchToMainThread();result=Result.Failure(Error("platform.initialize.exception"));}
            if(!stopped)State=result.IsSuccess?PlatformState.Ready:PlatformState.Failed;loaded.TrySetResult(result);
        }
        async UniTask<Result> ObserveLoad()
        {while(true){var e=Interrupt(loadWait,loadStart);if(e.HasValue){loadAbandoned=true;return Result.Failure(e.Value);}if(loaded.Task.Status!=UniTaskStatus.Pending)return await loaded.Task;await UniTask.Yield();}}
        async UniTask Pump()
        {
            try{while(pumping){if(State==PlatformState.Ready)Drain(false,limits.CallbacksPerFrame);await UniTask.Yield();}}
            finally{Drain(true,int.MaxValue);pumpEnded.TrySetResult();}
        }
        void Drain(bool discard,int maximum)
        {
            var count=Math.Min(QueuedCount,maximum);
            for(var i=0;i<count;i++)
            {
                KeyValuePair<long,PlatformCallback> item;lock(gate){if(inbox.Count==0)break;item=inbox.Dequeue();}
                try{if(discard||stopped||item.Value.Generation!=Generation){item.Value.Discard();discarded++;}else Dispatch(item.Value);}
                catch(Exception){item.Value.Discard();callbackErrors++;}
                finally{processed=Math.Max(processed,item.Key);}
            }
        }
        void Dispatch(PlatformCallback callback)
        {
            if(callback.Kind==PlatformCallbackKind.Login){DispatchLogin(callback);return;}
            if(callback.Sequence<=lastSequence){discarded++;return;}lastSequence=callback.Sequence;
            if(callback.Kind==PlatformCallbackKind.Capabilities)
            {rawCapabilities=callback.Capabilities;RecomputeCapabilities(true);return;}
            if(rawCapabilities.Get(PlatformFeature.Lifecycle).Support!=CapabilitySupport.Supported){discarded++;return;}
            var kind=callback.Lifecycle;var next=kind==PlatformLifecycleKind.Shown?PlatformVisibility.Foreground:kind==PlatformLifecycleKind.Hidden?PlatformVisibility.Background:Visibility;
            if((kind==PlatformLifecycleKind.Shown||kind==PlatformLifecycleKind.Hidden)&&next==Visibility){discarded++;return;}
            Visibility=next;
            if(kind==PlatformLifecycleKind.Hidden)
                foreach(var op in operations.ToArray())if((Capabilities.Get(PlatformFeature.LoginCredential).Constraints&CapabilityConstraints.ForegroundOnly)!=0)Cancel(op,Error("platform.login.hidden",ErrorKind.Canceled));
            RecomputeCapabilities(true);if(stopped)return;
            if(!lifecycle.Publish(new PlatformLifecycleEvent(Generation,callback.Sequence,kind,Visibility)).IsSuccess)callbackErrors++;
        }
        void RecomputeCapabilities(bool publish)
        {
            var list=new List<PlatformCapability>();foreach(PlatformFeature feature in Enum.GetValues(typeof(PlatformFeature)))
            {
                var cap=rawCapabilities.Get(feature);
                if(Visibility!=PlatformVisibility.Foreground&&cap.Support==CapabilitySupport.Supported&&cap.Availability==CapabilityAvailability.Available&&(cap.Constraints&CapabilityConstraints.ForegroundOnly)!=0)
                    cap=new PlatformCapability(feature,cap.Support,CapabilityAvailability.Unavailable,"platform.foreground.required",cap.Constraints,cap.MaxConcurrent);
                list.Add(cap);
            }
            Capabilities=new PlatformCapabilities(list);if(publish&&!capabilityEvents.Publish(Capabilities).IsSuccess)callbackErrors++;
        }
        public Result<ICleanupTicket> SubscribeLifecycle(IScope scope,string id,Action<PlatformLifecycleEvent> listener)
        {Main();if(State!=PlatformState.Ready||stopped)return Result<ICleanupTicket>.Failure(Error("platform.not_ready",ErrorKind.NotReady));return lifecycle.Subscribe(scope,id,listener);}
        public Result<ICleanupTicket> SubscribeCapabilities(IScope scope,string id,Action<PlatformCapabilities> listener)
        {Main();if(State!=PlatformState.Ready||stopped)return Result<ICleanupTicket>.Failure(Error("platform.not_ready",ErrorKind.NotReady));return capabilityEvents.Subscribe(scope,id,listener);}
        FrameworkError? LoginGate(PlatformLoginOptions options)
        {
            if(State!=PlatformState.Ready||stopped)return Error("platform.not_ready",ErrorKind.NotReady);
            var cap=Capabilities.Get(PlatformFeature.LoginCredential);
            if(cap.Support==CapabilitySupport.Unsupported)return Error("platform.login.unsupported",ErrorKind.NotSupported);
            if(cap.Availability==CapabilityAvailability.PermissionDenied)return Error("platform.login.permission",ErrorKind.PermissionDenied);
            if(!cap.IsUsable)return Error("platform.login.unavailable",ErrorKind.Unavailable);
            if((cap.Constraints&CapabilityConstraints.UserGestureRequired)!=0&&!options.UserInitiated)return Error("platform.login.gesture",ErrorKind.InvalidArgument);
            return null;
        }
        public Result<IPlatformLoginOperation> CaptureLogin(PlatformLoginOptions options)
        {
            Main();var error=LoginGate(options);if(error.HasValue)return Result<IPlatformLoginOperation>.Failure(error.Value);
            credentials.RemoveAll(c=>c.IsDisposed);
            if(operations.Count>=limits.MaxOperations||credentials.Count>=limits.MaxCredentials)return Result<IPlatformLoginOperation>.Failure(Error("platform.login.capacity",ErrorKind.Unavailable));
            var op=new Login(this,options);operations.Add(op);return Result<IPlatformLoginOperation>.Success(op);
        }
        void Finish(Login op,Result<PlatformLoginCredential> result)
        {
            if(Check(op)||op.Done.Task.Status!=UniTaskStatus.Pending)return;
            op.State=result.IsSuccess?PlatformLoginState.Succeeded:PlatformLoginState.Failed;op.Done.TrySetResult(result);
        }
        bool Check(Login op)
        {var e=stopped?Error("platform.owner.closed",ErrorKind.Canceled):(FrameworkError?)Interrupt(op.Wait,op.Start);if(e.HasValue)Cancel(op,e.Value);return op.Abandoned;}
        void Cancel(Login op,FrameworkError error)
        {
            if(op.Done.Task.Status==UniTaskStatus.Pending){op.Abandoned=true;op.State=error.Kind==ErrorKind.Timeout?PlatformLoginState.TimedOut:PlatformLoginState.Canceled;op.Done.TrySetResult(Result<PlatformLoginCredential>.Failure(error));}
            if(op.Ticket!=null&&!op.ActualEnded&&!op.CancelSent)
            {op.CancelSent=true;try{var r=op.Ticket.Cancel();if(!r.IsSuccess)op.CancelError=Clean(r.Error);}catch(Exception){op.CancelError=Error("platform.login.cancel_exception");}}
            if(!op.Started){operations.Remove(op);op.Ended.TrySetResult();}
        }
        void DispatchLogin(PlatformCallback callback)
        {
            var op=operations.FirstOrDefault(o=>o.AttemptId==callback.AttemptId&&o.Started);
            if(op==null||Check(op)||op.Done.Task.Status!=UniTaskStatus.Pending){callback.Discard();discarded++;return;}
            var result=callback.LoginResult;if(!result.IsSuccess)result=Result<PlatformLoginCredential>.Failure(Clean(result.Error));
            if(result.IsSuccess)
            {
                var value=result.Value;
                if(value.PlatformId!=Descriptor.Id||value.Mode!=Descriptor.Mode||value.AttemptId!=op.AttemptId||!value.ReadAuthorizationCode().IsSuccess||!value.TryClaim())
                {callback.Discard();Finish(op,Result<PlatformLoginCredential>.Failure(Error("platform.login.credential_invalid",ErrorKind.Integrity)));return;}
                credentials.Add(value);
            }
            Finish(op,result);if(result.IsSuccess&&op.State!=PlatformLoginState.Succeeded){result.Value.Dispose();credentials.Remove(result.Value);}
        }
        async UniTask<bool> Release(Login op,CleanupContext cleanup)
        {
            Result result;try{result=await op.Ticket.CleanupAsync(cleanup);await UniTask.SwitchToMainThread();}catch(Exception){await UniTask.SwitchToMainThread();result=Result.Failure(Error("platform.login.cleanup_exception",ErrorKind.CleanupFailed));}
            if(!result.IsSuccess){op.CleanupError=Clean(result.Error);return false;}op.CleanupError=null;op.Ticket=null;if(active==op)active=null;return true;
        }
        async UniTask Drive(Login op)
        {
            try
            {
                active=op;op.Ticket=session.CreateLogin(op.AttemptId,op.Options);if(op.Ticket==null)throw new InvalidOperationException();
                if(Check(op)){op.ActualEnded=true;}
                else
                {
                    op.State=PlatformLoginState.Starting;Result ended;
                    try{ended=await op.Ticket.StartAsync();await UniTask.SwitchToMainThread();}catch(Exception){await UniTask.SwitchToMainThread();ended=Result.Failure(Error("platform.login.start_exception"));}
                    op.ActualEnded=true;long fence;lock(gate)fence=accepted;
                    while(!stopped&&processed<fence){Check(op);await UniTask.Yield();}
                    if(op.Done.Task.Status==UniTaskStatus.Pending)Finish(op,Result<PlatformLoginCredential>.Failure(ended.IsSuccess?Error("platform.login.no_callback"):Clean(ended.Error)));
                }
            }
            catch(Exception){Finish(op,Result<PlatformLoginCredential>.Failure(Error("platform.login.factory_exception")));op.ActualEnded=true;}
            finally
            {
                if(op.Ticket!=null)await Release(op,releasePolicy);
                if(op.Ticket==null){operations.Remove(op);if(active==op)active=null;}op.Ended.TrySetResult();
            }
        }
        sealed class Login:IPlatformLoginOperation
        {
            readonly PlatformService owner;internal readonly PlatformLoginOptions Options;internal readonly UniTaskCompletionSource<Result<PlatformLoginCredential>> Done=new UniTaskCompletionSource<Result<PlatformLoginCredential>>();internal readonly UniTaskCompletionSource Ended=new UniTaskCompletionSource();
            internal OperationContext Wait;internal long Start;internal bool Started,Abandoned,ActualEnded,CancelSent;internal IPlatformLoginAttempt Ticket;internal UniTask Driver;
            public string AttemptId {get;}=Guid.NewGuid().ToString("N");public PlatformLoginState State {get;internal set;}=PlatformLoginState.Captured;public FrameworkError? CancelError {get;internal set;}public FrameworkError? CleanupError {get;internal set;}
            internal Login(PlatformService owner,PlatformLoginOptions options){this.owner=owner;Options=options;}
            public async UniTask<Result<PlatformLoginCredential>> WaitAsync(OperationContext wait)
            {
                Main();if(Started)return Result<PlatformLoginCredential>.Failure(Error("platform.login.single_use",ErrorKind.InvalidState));Started=true;Wait=wait;Start=Stopwatch.GetTimestamp();
                if(owner.Check(this)){owner.operations.Remove(this);Ended.TrySetResult();return await Done.Task;}
                owner.credentials.RemoveAll(c=>c.IsDisposed);
                var gate=owner.LoginGate(Options);if(!gate.HasValue&&owner.credentials.Count>=owner.limits.MaxCredentials)gate=Error("platform.login.capacity",ErrorKind.Unavailable);if(gate.HasValue||owner.active!=null)
                {owner.Finish(this,Result<PlatformLoginCredential>.Failure(gate??Error("platform.login.busy",ErrorKind.Unavailable)));owner.operations.Remove(this);Ended.TrySetResult();return await Done.Task;}
                Driver=owner.Drive(this).Preserve();
                while(true){owner.Check(this);if(Done.Task.Status!=UniTaskStatus.Pending)return await Done.Task;await UniTask.Yield();}
            }
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup)
        {
            Main();if(State==PlatformState.Closed)return UniTask.FromResult(Result.Success());stopped=true;State=PlatformState.Stopping;lock(gate)accepting=false;
            foreach(var op in operations.ToArray())Cancel(op,Error("platform.owner.closed",ErrorKind.Canceled));
            foreach(var value in credentials)value.Dispose();credentials.Clear();lifecycle.Dispose();capabilityEvents.Dispose();Drain(true,int.MaxValue);pumping=false;return UniTask.FromResult(Result.Success());
        }
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            Main();var retry=cleanupEntered;cleanupEntered=true;await StopAsync(cleanup);if(loaded!=null)await loaded.Task;if(initialized)await pumpEnded.Task;
            foreach(var op in operations.ToArray())await op.Ended.Task;
            if(retry)foreach(var op in operations.ToArray())if(op.Ticket!=null&&op.ActualEnded&&await Release(op,cleanup))operations.Remove(op);
            if(operations.Count>0)return Result.Failure(Error("platform.cleanup.attempts",ErrorKind.CleanupFailed));
            if(session!=null)
            {
                Result result;try{result=await session.CleanupAsync(cleanup);if(!result.IsSuccess)result=Result.Failure(Clean(result.Error));await UniTask.SwitchToMainThread();}catch(Exception){await UniTask.SwitchToMainThread();result=Result.Failure(Error("platform.cleanup.session_exception",ErrorKind.CleanupFailed));}
                sessionFailed=!result.IsSuccess;if(!result.IsSuccess)return result;session=null;
            }
            State=PlatformState.Closed;return Result.Success();
        }
        public DiagnosticCounters ReadDiagnostics(){Main();return new DiagnosticCounters{PendingOperations=PendingCount,QueuedCallbacks=QueuedCount,Listeners=lifecycle.ListenerCount+capabilityEvents.ListenerCount,FailedCleanup=operations.Count(o=>o.CleanupError.HasValue)+(sessionFailed?1:0)};}
    }
}
