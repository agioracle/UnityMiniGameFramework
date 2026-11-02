using System;
using MiniFramework.Contracts;
namespace MiniFramework.Providers.Fake
{
    public sealed class FakeAsset<T> : IProviderAsset<T> where T:class
    {
        private T value;
        private int attempt;
        private readonly string id;
        public ResourceKey Key { get; }
        public AssetLeaseState State { get; private set; }=AssetLeaseState.Active;
        public T Value {get{FakeThread.Check();if(State!=AssetLeaseState.Active)throw new InvalidOperationException("Asset is not available.");return value;}}
        public ControlledOperation<Unit> ReleaseAttempt { get; private set; }
        public CleanupContext LastCleanup { get; private set; }
        internal FakeAsset(string id,ResourceKey key,T value){this.id=id;Key=key;this.value=value;}
        public Result<IProviderOperation<Unit>> BeginRelease(CleanupContext cleanup)
        {
            FakeThread.Check();if(State==AssetLeaseState.Releasing)return Result<IProviderOperation<Unit>>.Success(ReleaseAttempt);
            if(State!=AssetLeaseState.Active)return FakeThread.Reject<IProviderOperation<Unit>>("fake.asset.release.state");
            return Start(cleanup);
        }
        public Result<IProviderOperation<Unit>> RetryRelease(CleanupContext cleanup)
        {FakeThread.Check();if(State!=AssetLeaseState.ReleaseFailed)return FakeThread.Reject<IProviderOperation<Unit>>("fake.asset.retry.state");return Start(cleanup);}
        private Result<IProviderOperation<Unit>> Start(CleanupContext cleanup)
        {
            LastCleanup=cleanup;State=AssetLeaseState.Releasing;
            ReleaseAttempt=new ControlledOperation<Unit>(id+"/release/"+(++attempt),result=>{State=result.IsSuccess?AssetLeaseState.Released:AssetLeaseState.ReleaseFailed;if(result.IsSuccess)value=null;});
            return Result<IProviderOperation<Unit>>.Success(ReleaseAttempt);
        }
    }
}
