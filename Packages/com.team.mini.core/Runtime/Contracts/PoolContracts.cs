using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public enum PoolRentState { Pending, Active, Returning, ReturnFailed, Returned }
    public interface IPoolRent<T> where T:class
    {
        string Id { get; }
        T Value { get; }
        IScope RentScope { get; }
        PoolRentState State { get; }
        UniTask<Result> ReturnAsync(CleanupContext cleanup);
        UniTask<Result> RetryReturnAsync(CleanupContext cleanup);
    }
    public interface IPoolService<T> where T:class
    {
        Result<IOwnedOperation<IPoolRent<T>>> Spawn(ResourceKey prefab,IScope owner,ResourcePriority priority=ResourcePriority.Normal);
        UniTask<Result> PrewarmAsync(ResourceKey prefab,int count,IScope owner,OperationContext wait);
        // Eviction refuses active/in-flight work. Repeating after failure retries retained cleanup.
        UniTask<Result> EvictAsync(ResourceKey prefab,CleanupContext cleanup);
    }
    public sealed class PoolCapacity
    {
        public int MaxInstancesPerBucket { get; }
        public int MaxIdlePerBucket { get; }
        public int MaxBuckets { get; }
        public PoolCapacity(int maxInstancesPerBucket,int maxIdlePerBucket,int maxBuckets=64)
        {
            if(maxInstancesPerBucket<=0||maxIdlePerBucket<0||maxIdlePerBucket>maxInstancesPerBucket||maxBuckets<=0)
                throw new System.ArgumentOutOfRangeException(nameof(maxInstancesPerBucket));
            MaxInstancesPerBucket=maxInstancesPerBucket;MaxIdlePerBucket=maxIdlePerBucket;MaxBuckets=maxBuckets;
        }
    }
}
