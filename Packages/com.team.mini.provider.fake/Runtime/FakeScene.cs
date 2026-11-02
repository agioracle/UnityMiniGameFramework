using MiniFramework.Contracts;
namespace MiniFramework.Providers.Fake
{
    public sealed class FakeScene : IProviderScene
    {
        private int attempt;
        private readonly string id;
        private bool unloadFailed;
        public ResourceKey Key { get; }
        public SceneLoadOptions Options { get; }
        public SceneLeaseState State { get; private set; }
        public bool HoldsDependencies => State!=SceneLeaseState.Released;
        public ControlledOperation<Unit> Activation { get; private set; }
        public ControlledOperation<Unit> Unload { get; private set; }
        public CleanupContext LastCleanup { get; private set; }
        internal FakeScene(string id,ResourceKey key,SceneLoadOptions options){this.id=id;Key=key;Options=options;State=options.ActivateOnLoad?SceneLeaseState.Active:SceneLeaseState.AwaitingActivation;}
        public Result<IProviderOperation<Unit>> BeginActivation()
        {
            FakeThread.Check();
            if(Activation!=null&&Activation.State==BackendState.Running)return Result<IProviderOperation<Unit>>.Success(Activation);
            if(State!=SceneLeaseState.AwaitingActivation)return FakeThread.Reject<IProviderOperation<Unit>>("fake.scene.activate.state");
            Activation=new ControlledOperation<Unit>(id+"/activate",result=>State=result.IsSuccess?SceneLeaseState.Active:SceneLeaseState.Failed);
            return Result<IProviderOperation<Unit>>.Success(Activation);
        }
        public Result<IProviderOperation<Unit>> BeginUnload(CleanupContext cleanup)
        {
            FakeThread.Check();if(State==SceneLeaseState.Unloading)return Result<IProviderOperation<Unit>>.Success(Unload);
            if(State==SceneLeaseState.Released||unloadFailed||(Activation!=null&&Activation.State==BackendState.Running))return FakeThread.Reject<IProviderOperation<Unit>>("fake.scene.unload.state");
            return StartUnload(cleanup);
        }
        public Result<IProviderOperation<Unit>> RetryUnload(CleanupContext cleanup)
        {FakeThread.Check();if(!unloadFailed||State!=SceneLeaseState.Failed)return FakeThread.Reject<IProviderOperation<Unit>>("fake.scene.retry.state");return StartUnload(cleanup);}
        private Result<IProviderOperation<Unit>> StartUnload(CleanupContext cleanup)
        {
            LastCleanup=cleanup;State=SceneLeaseState.Unloading;unloadFailed=false;
            Unload=new ControlledOperation<Unit>(id+"/unload/"+(++attempt),result=>{unloadFailed=!result.IsSuccess;State=result.IsSuccess?SceneLeaseState.Released:SceneLeaseState.Failed;});
            return Result<IProviderOperation<Unit>>.Success(Unload);
        }
    }
}
