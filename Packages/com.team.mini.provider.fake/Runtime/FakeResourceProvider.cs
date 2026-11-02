using System;
using System.Collections.Generic;
using MiniFramework.Contracts;
namespace MiniFramework.Providers.Fake
{
    public sealed class FakeResourceProvider : IAssetProvider, ISceneProvider
    {
        private int next;
        private readonly List<object> assetLoads=new List<object>();
        private readonly List<SceneLoad> sceneLoads=new List<SceneLoad>();
        public string Id { get; }
        public bool SupportsDeferredActivation { get; }
        public int AssetStartCount=>assetLoads.Count;
        public int SceneStartCount=>sceneLoads.Count;
        public FakeResourceProvider(string id="fake",bool supportsDeferredActivation=true)
        {FakeThread.Check();if(string.IsNullOrWhiteSpace(id))throw new ArgumentException(nameof(id));Id=id;SupportsDeferredActivation=supportsDeferredActivation;}
        public Result<IProviderOperation<IProviderAsset<T>>> BeginLoad<T>(ResourceKey key) where T:class
        {
            FakeThread.Check();if(!key.IsValid||key.Provider!=Id||key.Kind!=ResourceKind.Asset||key.AssetType!=typeof(T))return FakeThread.Reject<IProviderOperation<IProviderAsset<T>>>("fake.asset.key",ErrorKind.InvalidArgument);
            var load=new AssetLoad<T>("asset/"+(++next),key);assetLoads.Add(load);return Result<IProviderOperation<IProviderAsset<T>>>.Success(load.Operation);
        }
        public AssetLoad<T> AssetAt<T>(int index) where T:class {FakeThread.Check();return (AssetLoad<T>)assetLoads[index];}
        public Result<IProviderOperation<IProviderScene>> BeginLoadScene(ResourceKey key,SceneLoadOptions options)
        {
            FakeThread.Check();if(!key.IsValid||key.Provider!=Id||key.Kind!=ResourceKind.Scene)return FakeThread.Reject<IProviderOperation<IProviderScene>>("fake.scene.key",ErrorKind.InvalidArgument);
            if(!options.ActivateOnLoad&&!SupportsDeferredActivation)return FakeThread.Reject<IProviderOperation<IProviderScene>>("fake.scene.deferred.unsupported",ErrorKind.NotSupported);
            var load=new SceneLoad("scene/"+(++next),key,options);sceneLoads.Add(load);return Result<IProviderOperation<IProviderScene>>.Success(load.Operation);
        }
        public SceneLoad SceneAt(int index){FakeThread.Check();return sceneLoads[index];}
        public sealed class AssetLoad<T> where T:class
        {
            private readonly ResourceKey key;
            public ControlledOperation<IProviderAsset<T>> Operation {get;}
            public FakeAsset<T> Asset {get;private set;}
            internal AssetLoad(string id,ResourceKey key){this.key=key;Operation=new ControlledOperation<IProviderAsset<T>>(id);}
            public bool Succeed(T value)
            {
                FakeThread.Check();if(Operation.State!=BackendState.Running)return false;
                if(value==null)throw new ArgumentNullException(nameof(value));
                Asset=new FakeAsset<T>(Operation.Id,key,value);return Operation.Complete(Result<IProviderAsset<T>>.Success(Asset));
            }
            public bool Fail(FrameworkError error)=>Operation.Complete(Result<IProviderAsset<T>>.Failure(error));
        }
        public sealed class SceneLoad
        {
            private readonly ResourceKey key;private readonly SceneLoadOptions options;
            public ControlledOperation<IProviderScene> Operation {get;}
            public FakeScene Scene {get;private set;}
            internal SceneLoad(string id,ResourceKey key,SceneLoadOptions options){this.key=key;this.options=options;Operation=new ControlledOperation<IProviderScene>(id);}
            public bool Succeed(){FakeThread.Check();if(Operation.State!=BackendState.Running)return false;Scene=new FakeScene(Operation.Id,key,options);return Operation.Complete(Result<IProviderScene>.Success(Scene));}
            public bool Fail(FrameworkError error)=>Operation.Complete(Result<IProviderScene>.Failure(error));
        }
    }
}
