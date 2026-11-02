using System;
namespace MiniFramework.Contracts
{
    public enum ResourceLane { Download, Load }
    public enum ResourcePriority { Prefetch, Normal, Critical }
    public enum ResourceWorkState { Queued, Running, Completed }
    public interface IScheduledWork
    {
        ResourceWorkState SchedulingState { get; }
        // Only queued work can be withdrawn. Running work retains its actual-completion quota.
        bool CancelQueued();
        bool Promote(ResourcePriority priority);
    }
    public interface IScheduledOperation<T> : IProviderOperation<T>, IScheduledWork { }
    public interface IResourceWorkScheduler
    {
        Result<IScheduledOperation<T>> Schedule<T>(ResourceLane lane, Func<Result<IProviderOperation<T>>> start,
            ResourcePriority priority = ResourcePriority.Normal, Func<bool> stillWanted = null);
        // One synchronous, non-preemptible work unit. The producer owns any created result.
        Result<IScheduledOperation<T>> ScheduleFrame<T>(Func<Result<T>> work,
            ResourcePriority priority = ResourcePriority.Normal, Func<bool> stillWanted = null);
    }
    // Optional two-stage backend. Prepared load must reject missing prerequisites, not silently redownload.
    public interface IAssetDownloadProvider
    {
        Result<IProviderOperation<Unit>> BeginDownload(ResourceKey key);
        Result<IProviderOperation<IProviderAsset<T>>> BeginLoadPrepared<T>(ResourceKey key) where T : class;
    }
    public sealed class ResourceScheduleBudget
    {
        public int Downloads { get; }
        public int Loads { get; }
        public int QueueCapacityPerLane { get; }
        public int MaxStartsPerLanePerTick { get; }
        public int FrameUnits { get; }
        public TimeSpan FrameTime { get; }
        public ResourceScheduleBudget(int downloads, int loads, int frameUnits, TimeSpan frameTime,
            int queueCapacityPerLane = 128, int maxStartsPerLanePerTick = 64)
        {
            if (downloads <= 0 || loads <= 0 || frameUnits <= 0 || frameTime <= TimeSpan.Zero ||
                queueCapacityPerLane <= 0 || maxStartsPerLanePerTick <= 0) throw new ArgumentOutOfRangeException(nameof(downloads));
            Downloads=downloads; Loads=loads; FrameUnits=frameUnits; FrameTime=frameTime;
            QueueCapacityPerLane=queueCapacityPerLane; MaxStartsPerLanePerTick=maxStartsPerLanePerTick;
        }
    }
}
