using System;
using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public enum ResourceKind { Asset, Scene }
    // Exact ordinal identity. Version is selected for the lifetime of a service context.
    public readonly struct ResourceKey : IEquatable<ResourceKey>
    {
        public string Provider { get; }
        public string Package { get; }
        public string ContentVersion { get; }
        public string Address { get; }
        public ResourceKind Kind { get; }
        public Type AssetType { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Provider)&&!string.IsNullOrWhiteSpace(Package)&&!string.IsNullOrWhiteSpace(ContentVersion)&&!string.IsNullOrWhiteSpace(Address)&&
            ((Kind==ResourceKind.Asset&&AssetType!=null&&!AssetType.IsValueType&&!AssetType.ContainsGenericParameters)||(Kind==ResourceKind.Scene&&AssetType==null));
        public ResourceKey(string provider,string package,string contentVersion,string address,Type assetType)
            :this(provider,package,contentVersion,address,ResourceKind.Asset,assetType){}
        private ResourceKey(string provider,string package,string version,string address,ResourceKind kind,Type assetType)
        {
            Provider=provider;Package=package;ContentVersion=version;Address=address;Kind=kind;AssetType=assetType;
            if(!IsValid)throw new ArgumentException("Complete resource identity required.");
        }
        public static ResourceKey Scene(string provider,string package,string version,string address)=>new ResourceKey(provider,package,version,address,ResourceKind.Scene,null);
        public bool Equals(ResourceKey other)=>Provider==other.Provider&&Package==other.Package&&ContentVersion==other.ContentVersion&&Address==other.Address&&Kind==other.Kind&&AssetType==other.AssetType;
        public override bool Equals(object obj)=>obj is ResourceKey other&&Equals(other);
        public override int GetHashCode()
        {
            unchecked{var hash=17;hash=hash*31+(Provider?.GetHashCode()??0);hash=hash*31+(Package?.GetHashCode()??0);hash=hash*31+(ContentVersion?.GetHashCode()??0);hash=hash*31+(Address?.GetHashCode()??0);hash=hash*31+(int)Kind;return hash*31+(AssetType?.GetHashCode()??0);}
        }
    }
    public enum AssetLeaseState { Active, Releasing, ReleaseFailed, Released }
    public enum SceneLeaseState { Loading, AwaitingActivation, Active, Unloading, Failed, Released }
    public enum SceneLoadMode { Single, Additive }
    public readonly struct SceneLoadOptions
    {
        public SceneLoadMode Mode { get; }
        public bool ActivateOnLoad { get; }
        public SceneLoadOptions(SceneLoadMode mode,bool activateOnLoad)
        {if(mode!=SceneLoadMode.Single&&mode!=SceneLoadMode.Additive)throw new ArgumentOutOfRangeException(nameof(mode));Mode=mode;ActivateOnLoad=activateOnLoad;}
    }
    public interface IAssetLease<T> where T:class
    {
        ResourceKey Key { get; }
        string Id { get; }
        string OwnerId { get; }
        AssetLeaseState State { get; }
        T Value { get; }
        // Explicit release joins a running attempt; repeated successful return is detectable.
        UniTask<Result> ReleaseAsync(CleanupContext cleanup);
        UniTask<Result> RetryReleaseAsync(CleanupContext cleanup);
    }
    public interface ISceneLease
    {
        ResourceKey Key { get; }
        string Id { get; }
        string OwnerId { get; }
        SceneLeaseState State { get; }
        SceneLoadOptions Options { get; }
        Result<IOwnedOperation<Unit>> BeginActivation();
        // Completion means confirmed unload, not merely dispatching an unload command.
        UniTask<Result> UnloadAsync(CleanupContext cleanup);
        UniTask<Result> RetryUnloadAsync(CleanupContext cleanup);
    }
    public interface IAssetsService
    {
        // Accepting transfers request cleanup responsibility to service and owner's registry.
        Result<IOwnedOperation<IAssetLease<T>>> Load<T>(ResourceKey key,IScope owner) where T:class;
        Result<IOwnedOperation<IAssetLease<T>>> Load<T>(ResourceKey key,IScope owner,ResourcePriority priority) where T:class;
    }
    public interface ISceneService
    {
        Result<IOwnedOperation<ISceneLease>> Load(ResourceKey key,SceneLoadOptions options,IScope owner);
    }
    // Provider operations report ACTUAL completion. A business wait cancellation must not settle these.
    public interface IProviderOperation<T>
    {
        string Id { get; }
        BackendState State { get; }
        UniTask<Result<T>> Completion { get; }
    }
    public interface IProviderAsset<T> where T:class
    {
        ResourceKey Key { get; }
        AssetLeaseState State { get; }
        T Value { get; }
        Result<IProviderOperation<Unit>> BeginRelease(CleanupContext cleanup);
        Result<IProviderOperation<Unit>> RetryRelease(CleanupContext cleanup);
    }
    public interface IProviderScene
    {
        ResourceKey Key { get; }
        SceneLoadOptions Options { get; }
        SceneLeaseState State { get; }
        bool HoldsDependencies { get; }
        Result<IProviderOperation<Unit>> BeginActivation();
        Result<IProviderOperation<Unit>> BeginUnload(CleanupContext cleanup);
        Result<IProviderOperation<Unit>> RetryUnload(CleanupContext cleanup);
    }
    public interface IAssetProvider
    {
        string Id { get; }
        Result<IProviderOperation<IProviderAsset<T>>> BeginLoad<T>(ResourceKey key) where T:class;
    }
    public interface ISceneProvider
    {
        bool SupportsDeferredActivation { get; }
        Result<IProviderOperation<IProviderScene>> BeginLoadScene(ResourceKey key,SceneLoadOptions options);
    }
}
