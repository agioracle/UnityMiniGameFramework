using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public interface IResourceTarget<T> where T:class {bool IsAlive {get;} T Value {get;set;}}
    public interface IResourceBinding
    {
        long Version {get;}
        UniTask<Result> SetAsync(ResourceKey key,OperationContext wait);
        UniTask<Result> CloseAsync(CleanupContext cleanup);
        UniTask<Result> RetryCleanupAsync(CleanupContext cleanup);
    }
    // Optional capability: promote a cold/shared request and its not-yet-started stages, never preempt.
    public interface IPrioritizedOperation {bool Promote(ResourcePriority priority);}
    public interface IPrefetchControl {IDisposable SuppressPrefetch();bool IsPrefetchSuppressed {get;}}
    public interface IResourceTrimTarget {UniTask<Result> TrimIdleAsync(int destroysPerFrame,CleanupContext cleanup);}
    public interface IMemoryPressureSource {IDisposable Subscribe(Action notification);}
    public interface IMemoryPressureHandler {UniTask<Result> NotifyAsync(OperationContext wait);}
    public readonly struct ResourceEntrySnapshot
    {
        public ResourceKey Key {get;} public int Interests {get;}
        public bool Pending {get;} public bool Loading {get;} public bool Downloading {get;}
        public bool Releasing {get;} public bool ReleaseFailed {get;}
        public ResourceEntrySnapshot(ResourceKey key,int interests,bool pending,bool loading,bool downloading,bool releasing,bool failed)
        {Key=key;Interests=interests;Pending=pending;Loading=loading;Downloading=downloading;Releasing=releasing;ReleaseFailed=failed;}
    }
    public readonly struct ResourceOwnerSnapshot
    {
        public ResourceKey Key {get;} public string RequestId {get;} public string OwnerId {get;}
        public ScopeState OwnerState {get;} public bool HasLease {get;} public bool Cleaning {get;}
        public ResourceOwnerSnapshot(ResourceKey key,string request,string owner,ScopeState state,bool lease,bool cleaning)
        {Key=key;RequestId=request;OwnerId=owner;OwnerState=state;HasLease=lease;Cleaning=cleaning;}
    }
    public sealed class AssetResourceSnapshot
    {
        public IReadOnlyList<ResourceEntrySnapshot> Entries {get;}
        public IReadOnlyList<ResourceOwnerSnapshot> Owners {get;}
        public AssetResourceSnapshot(IEnumerable<ResourceEntrySnapshot> entries,IEnumerable<ResourceOwnerSnapshot> owners)
        {Entries=new List<ResourceEntrySnapshot>(entries).AsReadOnly();Owners=new List<ResourceOwnerSnapshot>(owners).AsReadOnly();}
    }
    public interface IAssetResourceDiagnostics {AssetResourceSnapshot CaptureResources();}
    public readonly struct PoolBucketSnapshot
    {
        public ResourceKey Key {get;} public string OwnerId {get;}
        public int Active {get;} public int Idle {get;} public int Pending {get;} public int Destroying {get;}
        public bool Closing {get;} public int FailedCleanup {get;}
        public PoolBucketSnapshot(ResourceKey key,string owner,int active,int idle,int pending,int destroying,bool closing,int failed)
        {Key=key;OwnerId=owner;Active=active;Idle=idle;Pending=pending;Destroying=destroying;Closing=closing;FailedCleanup=failed;}
    }
    public readonly struct PoolRentSnapshot
    {
        public ResourceKey Key {get;} public string RequestId {get;} public string OwnerId {get;} public string RentScopeId {get;}
        public PoolRentState State {get;}
        public PoolRentSnapshot(ResourceKey key,string request,string owner,string rent,PoolRentState state)
        {Key=key;RequestId=request;OwnerId=owner;RentScopeId=rent;State=state;}
    }
    public sealed class PoolResourceSnapshot
    {
        public IReadOnlyList<PoolBucketSnapshot> Buckets {get;} public IReadOnlyList<PoolRentSnapshot> Rents {get;}
        public PoolResourceSnapshot(IEnumerable<PoolBucketSnapshot> buckets,IEnumerable<PoolRentSnapshot> rents)
        {Buckets=new List<PoolBucketSnapshot>(buckets).AsReadOnly();Rents=new List<PoolRentSnapshot>(rents).AsReadOnly();}
    }
    public interface IPoolResourceDiagnostics {PoolResourceSnapshot CapturePoolResources();}
}
