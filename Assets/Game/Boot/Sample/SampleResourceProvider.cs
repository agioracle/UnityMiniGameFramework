using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Game.Boot
{
    // Explicit LOCAL template backend. Resources inventory is engine-owned; no memory-release claim.
    public sealed class SampleResourceProvider:IAssetProvider
    {
        readonly bool revive;public SampleResourceProvider(bool includeRevive=false){revive=includeRevive;}
        internal const string ReviveAddress="ui/revive",RevivePath="MiniFrameworkReviveSample/ui/revive";
        internal static string PathFor(bool revive,string address)=>revive&&address.StartsWith("ui/",StringComparison.Ordinal)?"MiniFrameworkReviveSample/"+address:SampleContent.Root+address;
        public string Id=>SampleContent.Provider;
        public int RetainedHandleCount {get;private set;}
        public int LoadStartCount {get;private set;}
        public Result<IProviderOperation<IProviderAsset<T>>> BeginLoad<T>(ResourceKey key) where T:class
        {
            if(!PlayerLoopHelper.IsMainThread)return Reject<IProviderOperation<IProviderAsset<T>>>("sample.resource.thread",ErrorKind.InvalidState);
            if(!key.IsValid||key.Kind!=ResourceKind.Asset||key.Provider!=Id||key.Package!=SampleContent.Package||key.ContentVersion!=SampleContent.Version||!(SampleContent.Types.TryGetValue(key.Address,out var type)&&type==typeof(T)||revive&&key.Address==ReviveAddress&&typeof(T)==typeof(GameObject)))return Reject<IProviderOperation<IProviderAsset<T>>>("sample.resource.key",ErrorKind.InvalidArgument);
            var op=new Operation<IProviderAsset<T>>();LoadStartCount++;op.Keep(Load(key,op));return Result<IProviderOperation<IProviderAsset<T>>>.Success(op);
        }
        async UniTask Load<T>(ResourceKey key,Operation<IProviderAsset<T>> operation) where T:class
        {
            Result<IProviderAsset<T>> result;
            try
            {
                var request=Resources.LoadAsync(PathFor(revive,key.Address),typeof(T));while(!request.isDone)await UniTask.Yield();
                var value=request.asset;if(!value)result=Reject<IProviderAsset<T>>("sample.resource.missing",ErrorKind.Unavailable);
                else{RetainedHandleCount++;result=Result<IProviderAsset<T>>.Success(new Handle<T>(this,key,value));}
            }
            catch(Exception e){result=Result<IProviderAsset<T>>.Failure(new FrameworkError(ErrorKind.Provider,"sample.resource.exception",e.Message));}
            operation.Complete(result);
        }
        sealed class Handle<T>:IProviderAsset<T> where T:class
        {
            readonly SampleResourceProvider provider;Object value;
            public ResourceKey Key {get;}public AssetLeaseState State {get;private set;}=AssetLeaseState.Active;
            public T Value=>State==AssetLeaseState.Active?(T)(object)value:throw new InvalidOperationException("Sample asset released.");
            internal Handle(SampleResourceProvider provider,ResourceKey key,Object value){this.provider=provider;Key=key;this.value=value;}
            public Result<IProviderOperation<Unit>> BeginRelease(CleanupContext cleanup)
            {
                if(State!=AssetLeaseState.Active)return Reject<IProviderOperation<Unit>>("sample.resource.release.state",ErrorKind.InvalidState);
                value=null;State=AssetLeaseState.Released;provider.RetainedHandleCount--;var op=new Operation<Unit>();op.Complete(Result<Unit>.Success(default));return Result<IProviderOperation<Unit>>.Success(op);
            }
            public Result<IProviderOperation<Unit>> RetryRelease(CleanupContext cleanup)=>Reject<IProviderOperation<Unit>>("sample.resource.retry.state",ErrorKind.InvalidState);
        }
        internal static Result<T> Reject<T>(string code,ErrorKind kind)=>Result<T>.Failure(new FrameworkError(kind,code));
        internal sealed class Operation<T>:IProviderOperation<T>
        {
            readonly UniTaskCompletionSource<Result<T>> source=new UniTaskCompletionSource<Result<T>>();UniTask driver;
            public string Id {get;}=Guid.NewGuid().ToString("N");public BackendState State {get;private set;}=BackendState.Running;
            public UniTask<Result<T>> Completion=>source.Task;
            internal void Keep(UniTask task){driver=task.Preserve();}
            internal void Complete(Result<T> result){State=result.IsSuccess?BackendState.Succeeded:BackendState.Failed;source.TrySetResult(result);}
        }
    }
    public sealed class SamplePackageBackend:IPackageBackend
    {
        readonly bool revive;public SamplePackageBackend(bool includeRevive=false){revive=includeRevive;}
        public int PreparedResourceCount {get;private set;}
        public IPackageSession Create(PackageProfile profile,PackageCandidate candidate)=>new Session(this,profile,candidate);
        sealed class Session:IPackageSession
        {
            readonly SamplePackageBackend backend;readonly PackageProfile profile;readonly PackageCandidate candidate;
            readonly List<Object> inventory=new List<Object>();
            internal Session(SamplePackageBackend backend,PackageProfile profile,PackageCandidate candidate){this.backend=backend;this.profile=profile;this.candidate=candidate;}
            public async UniTask<Result<PackageManifest>> PrepareAsync()
            {
                try
                {
                    var request=Resources.LoadAsync<TextAsset>(SampleContent.Root+"firstplay");while(!request.isDone)await UniTask.Yield();
                    var text=request.asset as TextAsset;if(!text)throw new FormatException("first-play catalog missing");
                    var catalog=JsonUtility.FromJson<SampleCatalog>(text.text);
                    if(catalog==null||catalog.format!=1||catalog.package!=profile.Package||catalog.version!=candidate.Version||catalog.configVersion!=candidate.Version||catalog.schema!=profile.Schema||catalog.files==null||catalog.files.Length!=SampleContent.Types.Count)throw new FormatException("first-play catalog compatibility");
                    var available=new HashSet<string>(StringComparer.Ordinal);
                    foreach(var file in catalog.files)
                    {
                        if(file==null||!SampleContent.Types.TryGetValue(file.address,out var type)||!available.Add(file.address)||file.path!=SampleContent.Root+file.address)throw new FormatException("first-play entry");
                        var load=Resources.LoadAsync(SampleResourceProvider.PathFor(backend.revive,file.address),type);while(!load.isDone)await UniTask.Yield();var value=load.asset;
                        if(!value)throw new FormatException("first-play missing: "+file.address);
                        if(value is TextAsset bytes && MiniFramework.Config.ConfigModule<object>.Hash(bytes.bytes)!=file.sha256)throw new FormatException("first-play integrity: "+file.address);
                        inventory.Add(value);backend.PreparedResourceCount=inventory.Count;
                    }
                    if(backend.revive){var extra=Resources.LoadAsync<GameObject>(SampleResourceProvider.RevivePath);while(!extra.isDone)await UniTask.Yield();if(!extra.asset)throw new FormatException("revive first-play missing");inventory.Add(extra.asset);available.Add(SampleResourceProvider.ReviveAddress);backend.PreparedResourceCount=inventory.Count;}
                    foreach(var pair in SampleContent.Scenes){if(!Application.CanStreamedLevelBeLoaded(pair.Value))throw new FormatException("first-play scene not in build: "+pair.Value);available.Add(pair.Key);}
                    return Result<PackageManifest>.Success(new PackageManifest(profile.Package,candidate.Version,catalog.configVersion,catalog.schema,true,new[]{profile.ProgramVersion},available));
                }
                catch(Exception e){return Result<PackageManifest>.Failure(new FrameworkError(ErrorKind.Integrity,"sample.firstplay.invalid",e.Message));}
            }
            public UniTask<Result> CleanupAsync(){inventory.Clear();backend.PreparedResourceCount=0;return UniTask.FromResult(Result.Success());}
        }
    }
}
