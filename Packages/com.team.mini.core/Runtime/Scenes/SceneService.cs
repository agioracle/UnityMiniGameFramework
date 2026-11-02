using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Scenes
{
    // A scene instance has one request/owner. There is deliberately no address cache.
    public sealed class SceneService : ISceneService, IModule, IAsyncCleanup
    {
        readonly ISceneProvider provider;
        readonly string providerId, package, version;
        readonly IPackageReadiness readiness;
        readonly CleanupContext cleanup;
        readonly List<Request> requests = new List<Request>();
        Request single;
        bool ready, stopped, cleanupAttempted;
        public string Id { get; }
        public IReadOnlyList<string> Dependencies { get; }
        public int RetainedSceneCount => requests.Count;
        public bool NeedsRecovery => requests.Any(r => r.CleanupFailed);
        public SceneService(ISceneProvider provider, string providerId, string package, string version, CleanupContext cleanup,
            IPackageReadiness readiness = null, string packageModuleId = null, string id = "scenes")
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            if (new[]{providerId,package,version,id}.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Frozen scene identity required.");
            this.providerId=providerId;this.package=package;this.version=version;this.cleanup=cleanup;this.readiness=readiness;Id=id;
            Dependencies=Array.AsReadOnly(packageModuleId==null?Array.Empty<string>():new[]{packageModuleId});
        }
        internal static void Main() { if (!PlayerLoopHelper.IsMainThread) throw new InvalidOperationException("Scenes require the main thread."); }
        internal static Result Fail(string code, ErrorKind kind=ErrorKind.InvalidState) => Result.Failure(new FrameworkError(kind,code));
        internal static Result<T> Fail<T>(string code, ErrorKind kind=ErrorKind.InvalidState) => Result<T>.Failure(new FrameworkError(kind,code));
        internal static FrameworkError? Interrupt(OperationContext wait,long start)
        {
            if(wait.Cancellation.IsCancellationRequested)return new FrameworkError(ErrorKind.Canceled,"scene.wait.canceled");
            if(wait.Timeout!=System.Threading.Timeout.InfiniteTimeSpan && (Stopwatch.GetTimestamp()-start)/(double)Stopwatch.Frequency>=wait.Timeout.TotalSeconds)
                return new FrameworkError(ErrorKind.Timeout,"scene.wait.timeout");
            return null;
        }
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            Main();var interrupted=Interrupt(operation,Stopwatch.GetTimestamp());
            if(interrupted.HasValue)return UniTask.FromResult(Result.Failure(interrupted.Value));
            if(ready||stopped)return UniTask.FromResult(Fail("scene.initialize.state"));
            var registered=context.Cleanup.Register(this);ready=registered.IsSuccess;
            return UniTask.FromResult(ready?Result.Success():Result.Failure(registered.Error));
        }
        public Result<IOwnedOperation<ISceneLease>> Load(ResourceKey key,SceneLoadOptions options,IScope owner)
        {
            Main();if(!ready||stopped)return Fail<IOwnedOperation<ISceneLease>>("scene.not_ready",ErrorKind.NotReady);
            if(owner==null||owner.State!=ScopeState.Open)return Fail<IOwnedOperation<ISceneLease>>("scene.owner.closed");
            if(!key.IsValid||key.Kind!=ResourceKind.Scene||key.Provider!=providerId||key.Package!=package||key.ContentVersion!=version)
                return Fail<IOwnedOperation<ISceneLease>>("scene.key",ErrorKind.InvalidArgument);
            if(readiness!=null){var check=readiness.CheckReady(package,version);if(!check.IsSuccess)return Result<IOwnedOperation<ISceneLease>>.Failure(check.Error);}
            if(!options.ActivateOnLoad&&!provider.SupportsDeferredActivation)return Fail<IOwnedOperation<ISceneLease>>("scene.deferred.unsupported",ErrorKind.NotSupported);
            if(NeedsRecovery)return Fail<IOwnedOperation<ISceneLease>>("scene.recovery.required",ErrorKind.CleanupFailed);
            var request=new Request(this,key,options,owner);
            var ticket=owner.Cleanup.Register(new OwnerCleanup(request));if(!ticket.IsSuccess)return Result<IOwnedOperation<ISceneLease>>.Failure(ticket.Error);
            request.Cleanup=ticket.Value;requests.Add(request);return Result<IOwnedOperation<ISceneLease>>.Success(request);
        }
        Result Reserve(Request request)
        {
            if(stopped||request.Owner.State!=ScopeState.Open)return Fail("scene.owner.closed",ErrorKind.Canceled);
            if(NeedsRecovery)return Fail("scene.recovery.required",ErrorKind.CleanupFailed);
            if(single!=null || (request.Options.Mode==SceneLoadMode.Single&&requests.Any(r=>r!=request&&r.InFlight)))return Fail("scene.transition.busy");
            if(readiness!=null){var check=readiness.CheckReady(package,version);if(!check.IsSuccess)return check;}
            if(request.Options.Mode==SceneLoadMode.Single)single=request;
            return Result.Success();
        }
        void Forget(Request request){requests.Remove(request);if(single==request)single=null;}
        Result CheckPackage()=>readiness==null?Result.Success():readiness.CheckReady(package,version);
        async UniTask<Result> Commit(Request request)
        {
            var compatible=CheckPackage();if(!compatible.IsSuccess)return compatible;
            if(request.Options.Mode!=SceneLoadMode.Single)return Result.Success();
            // Logical Single replaces owned scenes only. Never unload external Bootstrap scenes.
            foreach(var old in requests.ToArray())
            {
                if(old==request||old.Lease==null)continue;
                var result=await old.Close(cleanup,false);if(!result.IsSuccess)return result;
            }
            if(single==request)single=null;
            return Result.Success();
        }
        public UniTask<Result> StopAsync(CleanupContext context){Main();stopped=true;ready=false;foreach(var r in requests.ToArray())r.Abandon();return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext context)=>UniTask.FromResult(Result.Success());
        public UniTask<Result> CleanupAsync(CleanupContext context){var retry=cleanupAttempted;cleanupAttempted=true;return Drain(context,retry);}
        async UniTask<Result> Drain(CleanupContext context,bool retry)
        {
            Main();await StopAsync(context);var success=true;
            foreach(var r in requests.ToArray())if(!(await r.Close(context,retry)).IsSuccess)success=false;
            return success?Result.Success():Fail("scene.cleanup.retained",ErrorKind.CleanupFailed);
        }
        public async UniTask<Result> RetryRecoveryAsync(CleanupContext context)
        {
            Main();var success=true;
            foreach(var r in requests.ToArray())if(r.CleanupFailed&&!(await r.Close(context,true)).IsSuccess)success=false;
            return success?Result.Success():Fail("scene.recovery.retained",ErrorKind.CleanupFailed);
        }
        sealed class OwnerCleanup:IAsyncCleanup
        {
            readonly Request request;bool attempted;
            public OwnerCleanup(Request request){this.request=request;}
            public string Id=>request.Id;
            public UniTask<Result> CleanupAsync(CleanupContext context){var retry=attempted;attempted=true;return request.Close(context,retry);}
        }
        sealed class Request:IOwnedOperation<ISceneLease>
        {
            internal readonly SceneService Service;internal readonly IScope Owner;
            internal readonly ResourceKey Key;internal readonly SceneLoadOptions Options;
            readonly UniTaskCompletionSource<Result<ISceneLease>> loaded=new UniTaskCompletionSource<Result<ISceneLease>>();
            UniTaskCompletionSource<Result> closing;
            internal Lease Lease;
            bool waited,started,abandoned; OperationContext? waitPolicy; long waitStarted; BackendState backendState=BackendState.Running;
            internal bool InFlight=>started&&(loaded.Task.Status==UniTaskStatus.Pending || (Lease!=null&&Lease.Activating));
            internal bool CleanupFailed=>closing!=null&&closing.Task.Status!=UniTaskStatus.Pending&&!closing.Task.GetAwaiter().GetResult().IsSuccess;
            public string Id {get;}="scene/"+Guid.NewGuid().ToString("N");
            public BackendState BackendState=>backendState;
            public ICleanupTicket Cleanup {get;internal set;}
            internal Request(SceneService service,ResourceKey key,SceneLoadOptions options,IScope owner){Service=service;Key=key;Options=options;Owner=owner;}
            internal bool Invalid=>abandoned||Service.stopped||Owner.State!=ScopeState.Open||(waitPolicy.HasValue&&Interrupt(waitPolicy.Value,waitStarted).HasValue);
            internal void Abandon(){abandoned=true;}
            public async UniTask<Result<ISceneLease>> WaitAsync(OperationContext wait)
            {
                Main();if(waited)return Fail<ISceneLease>("scene.wait.once");waited=true;var start=Stopwatch.GetTimestamp();
                var interrupted=Interrupt(wait,start);
                if(Invalid||interrupted.HasValue){Abandon();Service.Forget(this);return interrupted.HasValue?Result<ISceneLease>.Failure(interrupted.Value):Fail<ISceneLease>("scene.owner.closed",ErrorKind.Canceled);}
                var reserved=Service.Reserve(this);if(!reserved.IsSuccess){Abandon();Service.Forget(this);return Result<ISceneLease>.Failure(reserved.Error);}
                waitPolicy=wait;waitStarted=start;started=true;Run().Forget();
                while(true)
                {
                    interrupted=Interrupt(wait,start);
                    if(Service.stopped||Owner.State!=ScopeState.Open||interrupted.HasValue){Abandon();Close(Service.cleanup,false).Forget();return interrupted.HasValue?Result<ISceneLease>.Failure(interrupted.Value):Fail<ISceneLease>("scene.owner.closed",ErrorKind.Canceled);}
                    if(loaded.Task.Status!=UniTaskStatus.Pending){waitPolicy=null;return await loaded.Task;}
                    await UniTask.Yield();
                }
            }
            async UniTask Run()
            {
                Result<ISceneLease> result;
                try
                {
                    // Stage additively; native Single would irreversibly destroy unowned scenes.
                    var begin=Service.provider.BeginLoadScene(Key,new SceneLoadOptions(SceneLoadMode.Additive,Options.ActivateOnLoad));
                    if(!begin.IsSuccess){backendState=BackendState.Failed;result=Result<ISceneLease>.Failure(begin.Error);}
                    else
                    {
                        var raw=await begin.Value.Completion;await UniTask.SwitchToMainThread();backendState=raw.IsSuccess?BackendState.Succeeded:BackendState.Failed;
                        if(!raw.IsSuccess)result=Result<ISceneLease>.Failure(raw.Error);
                        else if(raw.Value==null)result=Fail<ISceneLease>("scene.payload.null",ErrorKind.Integrity);
                        else
                        {
                            Lease=new Lease(this,raw.Value);var compatible=Service.CheckPackage();
                            if(!raw.Value.Key.Equals(Key))result=Fail<ISceneLease>("scene.payload.key",ErrorKind.Integrity);
                            else if(Invalid)result=Fail<ISceneLease>("scene.load.abandoned",ErrorKind.Canceled);
                            else if(!compatible.IsSuccess)result=Result<ISceneLease>.Failure(compatible.Error);
                            else if(Options.ActivateOnLoad)
                            {
                                if(raw.Value.State!=SceneLeaseState.Active)result=Fail<ISceneLease>("scene.payload.state",ErrorKind.Integrity);
                                else{var commit=await Service.Commit(this);result=commit.IsSuccess?Result<ISceneLease>.Success(Lease):Result<ISceneLease>.Failure(commit.Error);}
                            }
                            else result=raw.Value.State==SceneLeaseState.AwaitingActivation?Result<ISceneLease>.Success(Lease):Fail<ISceneLease>("scene.deferred.violated",ErrorKind.Integrity);
                        }
                    }
                }
                catch(Exception e){backendState=BackendState.Failed;result=Result<ISceneLease>.Failure(new FrameworkError(ErrorKind.Provider,"scene.load.exception",e.Message));}
                loaded.TrySetResult(result);
                if(!result.IsSuccess||Invalid){Abandon();Close(Service.cleanup,false).Forget();}
            }
            internal UniTask<Result> Close(CleanupContext context,bool retry)
            {
                Main();Abandon();
                if(closing!=null && (closing.Task.Status==UniTaskStatus.Pending || !retry || closing.Task.GetAwaiter().GetResult().IsSuccess))return closing.Task;
                closing=new UniTaskCompletionSource<Result>();CloseDriver(context,retry,closing).Forget();return closing.Task;
            }
            async UniTask CloseDriver(CleanupContext context,bool retry,UniTaskCompletionSource<Result> completion)
            {
                Result result;
                try
                {
                    if(started)await loaded.Task;
                    result=Lease==null?Result.Success():await Lease.Close(context,retry);
                    if(result.IsSuccess)Service.Forget(this);
                }
                catch(Exception e){result=Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"scene.close.exception",e.Message));}
                completion.TrySetResult(result);
            }
        }
        sealed class Lease:ISceneLease
        {
            readonly Request request;readonly IProviderScene handle;
            Activation activation;UniTaskCompletionSource<Result> unload;bool unloadFailed;
            public string Id=>request.Id+"/lease";public string OwnerId=>request.Owner.Id;public ResourceKey Key=>request.Key;
            public SceneLoadOptions Options=>request.Options;public SceneLeaseState State=>handle.State;
            internal bool Activating=>activation!=null&&activation.Running;
            internal Lease(Request request,IProviderScene handle){this.request=request;this.handle=handle;}
            public Result<IOwnedOperation<Unit>> BeginActivation()
            {
                Main();if(request.Invalid||State!=SceneLeaseState.AwaitingActivation||activation!=null)return Fail<IOwnedOperation<Unit>>("scene.activate.state");
                var compatible=request.Service.CheckPackage();if(!compatible.IsSuccess)return Result<IOwnedOperation<Unit>>.Failure(compatible.Error);
                activation=new Activation(this);return Result<IOwnedOperation<Unit>>.Success(activation);
            }
            public UniTask<Result> UnloadAsync(CleanupContext context)
            {Main();if(State==SceneLeaseState.Released||unloadFailed)return UniTask.FromResult(Fail("scene.unload.state"));return request.Close(context,false);}
            public UniTask<Result> RetryUnloadAsync(CleanupContext context)
            {Main();if(!request.CleanupFailed)return UniTask.FromResult(Fail("scene.retry.state"));return request.Close(context,true);}
            internal UniTask<Result> Close(CleanupContext context,bool retry)
            {
                if(unload!=null&&(unload.Task.Status==UniTaskStatus.Pending||!retry||!unloadFailed))return unload.Task;
                unload=new UniTaskCompletionSource<Result>();Unload(context,retry,unload).Forget();return unload.Task;
            }
            async UniTask Unload(CleanupContext context,bool retry,UniTaskCompletionSource<Result> completion)
            {
                Result result;
                try
                {
                    if(Activating)await activation.actual.Task;
                    if(State==SceneLeaseState.Released)result=Result.Success();
                    else
                    {
                        var begin=retry&&unloadFailed?handle.RetryUnload(context):handle.BeginUnload(context);
                        if(!begin.IsSuccess)result=Result.Failure(begin.Error);
                        else{var raw=await begin.Value.Completion;result=raw.IsSuccess&&!handle.HoldsDependencies&&handle.State==SceneLeaseState.Released?Result.Success():raw.IsSuccess?Fail("scene.unload.unconfirmed",ErrorKind.Integrity):Result.Failure(raw.Error);}
                    }
                }
                catch(Exception e){result=Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"scene.unload.exception",e.Message));}
                unloadFailed=!result.IsSuccess;completion.TrySetResult(result);
            }
            sealed class Activation:IOwnedOperation<Unit>
            {
                readonly Lease lease;bool waited;internal bool Running;OperationContext policy;long waitStarted;BackendState backendState=BackendState.Running;
                internal readonly UniTaskCompletionSource<Result<Unit>> actual=new UniTaskCompletionSource<Result<Unit>>();
                public Activation(Lease lease){this.lease=lease;}
                public string Id=>lease.Id+"/activation";public ICleanupTicket Cleanup=>lease.request.Cleanup;
                public BackendState BackendState=>backendState;
                public async UniTask<Result<Unit>> WaitAsync(OperationContext wait)
                {
                    Main();if(waited)return Fail<Unit>("scene.activation.wait.once");waited=true;var start=Stopwatch.GetTimestamp();var error=Interrupt(wait,start);
                    if(error.HasValue||lease.request.Invalid)return error.HasValue?Result<Unit>.Failure(error.Value):Fail<Unit>("scene.owner.closed",ErrorKind.Canceled);
                    policy=wait;waitStarted=start;Running=true;Run().Forget();
                    while(true)
                    {
                        error=Interrupt(wait,start);
                        if(error.HasValue||lease.request.Service.stopped||lease.request.Owner.State!=ScopeState.Open||(lease.request.Invalid&&actual.Task.Status==UniTaskStatus.Pending)){lease.request.Abandon();lease.request.Close(lease.request.Service.cleanup,false).Forget();return error.HasValue?Result<Unit>.Failure(error.Value):Fail<Unit>("scene.owner.closed",ErrorKind.Canceled);}
                        if(actual.Task.Status!=UniTaskStatus.Pending)return await actual.Task;
                        await UniTask.Yield();
                    }
                }
                async UniTask Run()
                {
                    Result<Unit> result;
                    try
                    {
                        var begin=lease.handle.BeginActivation();result=begin.IsSuccess?await begin.Value.Completion:Result<Unit>.Failure(begin.Error);
                        backendState=result.IsSuccess?BackendState.Succeeded:BackendState.Failed;
                        if(Interrupt(policy,waitStarted).HasValue)lease.request.Abandon();
                        if(result.IsSuccess&&lease.handle.State!=SceneLeaseState.Active)result=Fail<Unit>("scene.activate.unconfirmed",ErrorKind.Integrity);
                        if(result.IsSuccess&&!lease.request.Invalid){var commit=await lease.request.Service.Commit(lease.request);if(!commit.IsSuccess)result=Result<Unit>.Failure(commit.Error);}
                    }
                    catch(Exception e){backendState=BackendState.Failed;result=Result<Unit>.Failure(new FrameworkError(ErrorKind.Provider,"scene.activate.exception",e.Message));}
                    Running=false;actual.TrySetResult(result);
                    if(!result.IsSuccess||lease.request.Invalid)lease.request.Close(lease.request.Service.cleanup,false).Forget();
                }
            }
        }
    }
}
