using System;
using System.Collections.Generic;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
namespace MiniFramework.Scenes
{
    public sealed class SessionFlow:ISessionFlow,IModule,IAsyncCleanup,IDiagnosticSource
    {
        readonly ISceneService scenes;readonly CleanupContext cleanup;
        readonly Scope root=new Scope("flow/"+Guid.NewGuid().ToString("N"));
        readonly List<Session> retiring=new List<Session>();
        UniTaskCompletionSource<Result<IGameSession>> transition;bool stopped,cleanupAttempted,rootCloseStarted;UniTask<CleanupReport> rootClose;
        public DiagnosticCounters ReadDiagnostics(){SceneService.Main();return root.ReadDiagnostics();}
        public string Id {get;}public IReadOnlyList<string> Dependencies {get;}
        public SessionFlowState State {get;private set;}
        public IGameSession Current {get;private set;}
        public SessionFlow(CleanupContext cleanup,ISceneService scenes=null,string sceneModuleId="scenes",string id="flow")
        {this.cleanup=cleanup;this.scenes=scenes;Id=id;Dependencies=Array.AsReadOnly(scenes==null?Array.Empty<string>():new[]{sceneModuleId});}
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            SceneService.Main();var error=SceneService.Interrupt(operation,Stopwatch.GetTimestamp());
            if(error.HasValue)return UniTask.FromResult(Result.Failure(error.Value));
            if(State!=SessionFlowState.Uninitialized||stopped)return UniTask.FromResult(SceneService.Fail("flow.initialize.state"));
            var registered=context.Cleanup.Register(this);if(registered.IsSuccess)State=SessionFlowState.Ready;
            return UniTask.FromResult(registered.IsSuccess?Result.Success():Result.Failure(registered.Error));
        }
        sealed class Intent {internal bool Abandoned;internal OperationContext Policy;internal long Started;internal bool Canceled=>Abandoned||SceneService.Interrupt(Policy,Started).HasValue;}
        sealed class Session:IGameSession
        {public string Id {get;}public IScope Scope {get;}public ISceneLease Scene {get;internal set;}internal bool Closing;internal Session(string id,IScope scope){Id=id;Scope=scope;}}
        public UniTask<Result<IGameSession>> EnterAsync(string id,ResourceKey? scene,OperationContext operation)
        {
            SceneService.Main();var start=Stopwatch.GetTimestamp();var error=SceneService.Interrupt(operation,start);
            if(error.HasValue)return UniTask.FromResult(Result<IGameSession>.Failure(error.Value));
            if(State!=SessionFlowState.Ready||stopped)return UniTask.FromResult(SceneService.Fail<IGameSession>("flow.not_ready",ErrorKind.NotReady));
            if(string.IsNullOrWhiteSpace(id))return UniTask.FromResult(SceneService.Fail<IGameSession>("flow.id",ErrorKind.InvalidArgument));
            if(scene.HasValue&&scenes==null)return UniTask.FromResult(SceneService.Fail<IGameSession>("flow.scenes.absent",ErrorKind.NotSupported));
            var child=root.CreateChild(id+"/"+Guid.NewGuid().ToString("N"),ScopeKind.Session);
            if(!child.IsSuccess)return UniTask.FromResult(Result<IGameSession>.Failure(child.Error));
            var candidate=new Session(id,child.Value);var intent=new Intent{Policy=operation,Started=start};State=SessionFlowState.Transitioning;
            transition=new UniTaskCompletionSource<Result<IGameSession>>();Run(candidate,scene,intent,transition).Forget();
            return Observe(transition.Task,intent,operation,start);
        }
        async UniTask<Result<IGameSession>> Observe(UniTask<Result<IGameSession>> task,Intent intent,OperationContext operation,long start)
        {
            while(true)
            {
                var error=SceneService.Interrupt(operation,start);
                if(error.HasValue||stopped){intent.Abandoned=true;return error.HasValue?Result<IGameSession>.Failure(error.Value):SceneService.Fail<IGameSession>("flow.stopped",ErrorKind.Canceled);}
                if(task.Status!=UniTaskStatus.Pending)return await task;
                await UniTask.Yield();
            }
        }
        async UniTask Run(Session candidate,ResourceKey? key,Intent intent,UniTaskCompletionSource<Result<IGameSession>> completed)
        {
            Result<IGameSession> result;bool committed=false;
            try
            {
                if(key.HasValue)
                {
                    var request=scenes.Load(key.Value,new SceneLoadOptions(SceneLoadMode.Additive,false),candidate.Scope);
                    if(!request.IsSuccess)throw new FlowFailure(request.Error);
                    var loaded=await request.Value.WaitAsync(OperationContext.Unlimited());
                    if(!loaded.IsSuccess)throw new FlowFailure(loaded.Error);candidate.Scene=loaded.Value;
                    if(intent.Canceled||stopped)throw new FlowFailure(new FrameworkError(ErrorKind.Canceled,"flow.abandoned"));
                    var activation=candidate.Scene.BeginActivation();if(!activation.IsSuccess)throw new FlowFailure(activation.Error);
                    var active=await activation.Value.WaitAsync(OperationContext.Unlimited());if(!active.IsSuccess)throw new FlowFailure(active.Error);
                }
                if(intent.Canceled||stopped)throw new FlowFailure(new FrameworkError(ErrorKind.Canceled,"flow.abandoned"));
                var old=Current as Session;Current=candidate;committed=true;
                if(old!=null){retiring.Add(old);var closed=await CloseSession(old,cleanup);if(!closed.IsSuccess)throw new FlowFailure(closed.Error);retiring.Remove(old);}
                result=Result<IGameSession>.Success(candidate);
            }
            catch(FlowFailure e){result=Result<IGameSession>.Failure(e.Error);}
            catch(Exception e){result=Result<IGameSession>.Failure(new FrameworkError(ErrorKind.Unexpected,"flow.transition.exception",e.Message));}
            if(!committed)
            {retiring.Add(candidate);if((await CloseSession(candidate,cleanup)).IsSuccess)retiring.Remove(candidate);}
            State=stopped?SessionFlowState.Closed:retiring.Count>0?SessionFlowState.Recovering:SessionFlowState.Ready;
            completed.TrySetResult(result);
        }
        static async UniTask<Result> CloseSession(Session session,CleanupContext cleanup)
        {
            var report=session.Closing?await session.Scope.RetryFailedAsync(cleanup):await session.Scope.CloseAsync(cleanup);session.Closing=true;
            while(report.PendingCount>0){await UniTask.Yield();report=session.Scope.Cleanup.Snapshot;}
            return report.IsFullyDrained?Result.Success():SceneService.Fail("flow.session.retained",ErrorKind.CleanupFailed);
        }
        public async UniTask<Result> RetryRecoveryAsync(CleanupContext context)
        {
            SceneService.Main();if(State!=SessionFlowState.Recovering)return SceneService.Fail("flow.retry.state");
            State=SessionFlowState.Transitioning;
            foreach(var session in retiring.ToArray())if((await CloseSession(session,context)).IsSuccess)retiring.Remove(session);
            State=stopped?SessionFlowState.Closed:retiring.Count==0?SessionFlowState.Ready:SessionFlowState.Recovering;
            return retiring.Count==0?Result.Success():SceneService.Fail("flow.recovery.retained",ErrorKind.CleanupFailed);
        }
        public UniTask<Result> StopAsync(CleanupContext context){SceneService.Main();stopped=true;Current=null;State=SessionFlowState.Closed;if(!rootCloseStarted){rootCloseStarted=true;rootClose=root.CloseAsync(context).Preserve();}return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext context)=>UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext context)
        {
            SceneService.Main();await StopAsync(context);if(transition!=null)await transition.Task;
            var retry=cleanupAttempted;cleanupAttempted=true;
            var report=retry?await root.RetryFailedAsync(context):await rootClose;
            while(report.PendingCount>0){await UniTask.Yield();report=root.Snapshot;}
            return report.IsFullyDrained?Result.Success():SceneService.Fail("flow.cleanup.retained",ErrorKind.CleanupFailed);
        }
        sealed class FlowFailure:Exception {internal FrameworkError Error;internal FlowFailure(FrameworkError error){Error=error;}}
    }
}
