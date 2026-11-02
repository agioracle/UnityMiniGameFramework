using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Access;
using Game.Config;
using Game.Config.Generated;
using MiniFramework.Config;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Providers.Fake;
using MiniFramework.Providers.Local;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace MiniFramework.Config.Tests
{
    public sealed class ConfigModuleTests
    {
        static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);
        sealed class Backend:IPackageBackend
        {
            internal PackageManifest Manifest=new PackageManifest("main","v1","v1",GeneratedConfigInfo.Schema,true,new[]{"app"},new[]{"config/catalog","config/tblevel","config/tbreward"});
            public IPackageSession Create(PackageProfile p,PackageCandidate c)=>new Session(Manifest);
            sealed class Session:IPackageSession
            {
                readonly PackageManifest manifest;internal Session(PackageManifest value){manifest=value;}
                public UniTask<Result<PackageManifest>> PrepareAsync()=>UniTask.FromResult(Result<PackageManifest>.Success(manifest));
                public UniTask<Result> CleanupAsync()=>UniTask.FromResult(Result.Success());
            }
        }
        sealed class Provider:IAssetProvider
        {
            internal readonly FakeResourceProvider Fake=new FakeResourceProvider();internal readonly List<ResourceKey> Keys=new List<ResourceKey>();
            public string Id=>"fake";
            public Result<IProviderOperation<IProviderAsset<T>>> BeginLoad<T>(ResourceKey key) where T:class{Keys.Add(key);return Fake.BeginLoad<T>(key);}
        }
        sealed class Env
        {
            internal readonly Backend Backend=new Backend();internal readonly Provider Provider=new Provider();
            internal readonly PackageStartup Package;internal readonly AssetsService Assets;internal readonly ConfigModule<Tables> Config;internal readonly GameContext Context;
            internal readonly List<TextAsset> Temporary=new List<TextAsset>();internal ConfigCatalog Catalog;internal bool Auto=true,FailRelease,CorruptData,FailLoad;internal int Limit=int.MaxValue;
            internal readonly EditorLocalAssetProvider Local;
            internal Env(bool local=false,Func<IReadOnlyDictionary<string,byte[]>,Tables> decode=null,ConfigDefinition definition=null)
            {
                Catalog=JsonUtility.FromJson<ConfigCatalog>(Asset("catalog").text);
                var profile=new PackageProfile("main","app",GeneratedConfigInfo.Schema,new[]{"config/catalog","config/tblevel","config/tbreward"},new[]{new PackageCandidate(PackageSource.BuiltIn,"v1","local/v1")});
                Package=new PackageStartup(profile,Backend);
                if(local)Local=new EditorLocalAssetProvider("main","v1",new Dictionary<string,string>{{"config/catalog",Path("catalog")},{"config/tblevel",Path("tblevel")},{"config/tbreward",Path("tbreward")}});
                IAssetProvider provider=local?(IAssetProvider)Local:Provider;
                Assets=new AssetsService(provider,"main","v1",Cleanup,packageReadiness:Package,packageModuleId:"package");
                Config=new ConfigModule<Tables>(Assets,Package,definition??GameConfigDecoder.Definition(provider.Id),decode??GameConfigDecoder.Decode,Cleanup);
                Context=new GameContext(new[]{new ModuleRegistration(Config.Id,Config.Dependencies,()=>Config,typeof(IConfigService<Tables>)),new ModuleRegistration(Assets.Id,Assets.Dependencies,()=>Assets,typeof(IAssetsService)),new ModuleRegistration(Package.Id,Package.Dependencies,()=>Package,typeof(IPackageReadiness),typeof(IConfigPackageReadiness))},Cleanup);
            }
            internal void Step()
            {
                for(var i=0;i<Provider.Keys.Count;i++)
                {
                    var request=Provider.Fake.AssetAt<TextAsset>(i);
                    if(Auto&&i<Limit&&request.Operation.State==BackendState.Running)
                    {
                        if(FailLoad)request.Fail(new FrameworkError(ErrorKind.Provider,"test.load"));
                        else
                        {
                            TextAsset value;
                            if(i==0){value=new TextAsset(JsonUtility.ToJson(Catalog));Temporary.Add(value);}
                            else if(CorruptData){value=new TextAsset("corrupt");Temporary.Add(value);}
                            else value=Asset(Provider.Keys[i].Address.Substring("config/".Length));
                            request.Succeed(value);
                        }
                    }
                    request.Asset?.ReleaseAttempt?.Complete(FailRelease?Result<Unit>.Failure(new FrameworkError(ErrorKind.CleanupFailed,"test.release")):Result<Unit>.Success(default));
                }
            }
            internal async UniTask<T> Pump<T>(UniTask<T> task){for(int i=0;task.Status==UniTaskStatus.Pending&&i<600;i++){Step();await UniTask.Yield();}Assert.AreNotEqual(UniTaskStatus.Pending,task.Status,"configuration did not finish");return await task;}
            internal async UniTask Frames(int count=5){for(int i=0;i<count;i++){Step();await UniTask.Yield();}}
            internal async UniTask Finish(){Auto=true;Limit=int.MaxValue;FailRelease=false;await Pump(Context.CloseAsync(OperationContext.Unlimited()));var closed=await Pump(Context.RetryCleanupAsync(Cleanup,OperationContext.Unlimited()));Assert.IsTrue(closed.IsSuccess);Assert.AreEqual(0,Assets.RetainedEntryCount);Assert.AreEqual(0,Assets.RetainedRequestCount);foreach(var asset in Temporary)Object.DestroyImmediate(asset);}
        }
        static string Path(string name)=>"Assets/Game/ConfigData/"+name+(name=="catalog"?".json":".bytes");
        static TextAsset Asset(string name)
        {
#if UNITY_EDITOR
            var value=UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(Path(name));Assert.IsNotNull(value);return value;
#else
            throw new NotSupportedException("Editor-imported binary fixture; no Player validation claim.");
#endif
        }
        readonly List<Env> environments=new List<Env>();
        Env New(bool local=false,Func<IReadOnlyDictionary<string,byte[]>,Tables> decode=null,ConfigDefinition definition=null){var value=new Env(local,decode,definition);environments.Add(value);return value;}
        [UnityTearDown] public IEnumerator TearDown()=>UniTask.ToCoroutine(async()=>{foreach(var env in environments)await env.Finish();environments.Clear();});
        [UnityTest] public IEnumerator RealLubanBinaryLoadsThroughLocalProvider()=>UniTask.ToCoroutine(async()=>{var e=New(true);Assert.IsTrue((await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()))).IsSuccess);var tables=e.Config.Get().Value;Assert.AreEqual(10,tables.TbLevel.Get(1).RewardId_Ref.Coins);Assert.AreEqual("levels/second",tables.TbLevel.Get(2).SceneAddress);Assert.AreEqual(3,e.Local.LoadStartCount);await e.Finish();environments.Remove(e);Assert.AreEqual(0,e.Local.RetainedHandleCount);});
        [UnityTest] public IEnumerator FacadeReadsTypedGeneratedTables()=>UniTask.ToCoroutine(async()=>{var e=New();var game=new GameServices(e.Context);Assert.IsFalse(game.ReadConfig<Tables>().IsSuccess);Assert.IsTrue((await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()))).IsSuccess);Assert.AreEqual(25,game.ReadConfig<Tables>().Value.TbReward.Get(2).Coins);});
        [UnityTest] public IEnumerator PartialFilesNeverPublishSnapshot()=>UniTask.ToCoroutine(async()=>{var e=New();e.Limit=2;var start=e.Context.StartAsync(OperationContext.Unlimited());await e.Frames(10);Assert.AreEqual(ConfigState.Loading,e.Config.State);Assert.IsFalse(e.Config.Get().IsSuccess);e.Limit=3;Assert.IsTrue((await e.Pump(start)).IsSuccess);});
        [UnityTest] public IEnumerator ContextCloseClearsPublishedSnapshot()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()));await e.Pump(e.Context.CloseAsync(OperationContext.Unlimited()));Assert.AreEqual(ConfigState.Closed,e.Config.State);Assert.IsFalse(e.Config.Get().IsSuccess);});
        [UnityTest] public IEnumerator CloseDuringLoadDrainsLateCompletion()=>UniTask.ToCoroutine(async()=>{var e=New();e.Auto=false;var start=e.Context.StartAsync(OperationContext.Unlimited());await e.Frames();var close=e.Context.CloseAsync(OperationContext.Unlimited());await e.Frames();e.Auto=true;Assert.IsTrue((await e.Pump(close)).IsSuccess);Assert.IsFalse((await e.Pump(start)).IsSuccess);Assert.IsFalse(e.Config.Get().IsSuccess);Assert.AreEqual(0,e.Assets.RetainedEntryCount);});
        [UnityTest] public IEnumerator CanceledStartupDoesNotPublishLateData()=>UniTask.ToCoroutine(async()=>{var e=New();e.Auto=false;using(var cancel=new CancellationTokenSource()){var start=e.Context.StartAsync(OperationContext.Unlimited(cancel.Token));await e.Frames();cancel.Cancel();e.Auto=true;Assert.IsFalse((await e.Pump(start)).IsSuccess);Assert.IsFalse(e.Config.Get().IsSuccess);}});
        [UnityTest] public IEnumerator ZeroDeadlineStartsNoProviderWork()=>UniTask.ToCoroutine(async()=>{var e=New();Assert.IsFalse((await e.Context.StartAsync(default)).IsSuccess);Assert.AreEqual(0,e.Provider.Keys.Count);});
        [UnityTest] public IEnumerator WrongPackageVersionRejectedBeforeAssets()=>UniTask.ToCoroutine(async()=>{var e=New(definition:GameConfigDecoder.Definition("fake",version:"v2"));Assert.IsFalse((await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()))).IsSuccess);Assert.AreEqual(0,e.Provider.Keys.Count);});
        [UnityTest] public IEnumerator WrongCompiledSchemaRejectedBeforeAssets()=>UniTask.ToCoroutine(async()=>{var e=New(definition:new ConfigDefinition("fake","main","v1","other","config/catalog",new[]{"tblevel","tbreward"}));Assert.IsFalse((await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()))).IsSuccess);Assert.AreEqual(0,e.Provider.Keys.Count);});
        [UnityTest] public IEnumerator PackageConfigMismatchBlocksStartup()=>UniTask.ToCoroutine(async()=>{var e=New();e.Backend.Manifest=new PackageManifest("main","v1","v2",GeneratedConfigInfo.Schema,true,new[]{"app"},new[]{"config/catalog","config/tblevel","config/tbreward"});Assert.IsFalse((await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()))).IsSuccess);Assert.AreEqual(0,e.Provider.Keys.Count);});
        [UnityTest] public IEnumerator CatalogSchemaMismatchRejected()=>BadCatalog(c=>c.schema="bad");
        [UnityTest] public IEnumerator CatalogContentVersionMismatchRejected()=>BadCatalog(c=>c.contentVersion="v2");
        [UnityTest] public IEnumerator CatalogConfigVersionMismatchRejected()=>BadCatalog(c=>c.configVersion="v2");
        [UnityTest] public IEnumerator CatalogPackageMismatchRejected()=>BadCatalog(c=>c.package="other");
        [UnityTest] public IEnumerator UnknownCatalogFormatRejected()=>BadCatalog(c=>c.format=2);
        [UnityTest] public IEnumerator DuplicateTableRejected()=>BadCatalog(c=>c.files[1].name=c.files[0].name);
        [UnityTest] public IEnumerator DuplicateAddressRejected()=>BadCatalog(c=>c.files[1].address=c.files[0].address);
        [UnityTest] public IEnumerator UnknownTableRejected()=>BadCatalog(c=>c.files[0].name="unknown");
        [UnityTest] public IEnumerator MissingTableRejected()=>BadCatalog(c=>c.files=c.files.Take(1).ToArray());
        [UnityTest] public IEnumerator OversizedCatalogEntryRejected()=>BadCatalog(c=>c.files[0].size=int.MaxValue);
        [UnityTest] public IEnumerator InvalidHashRejected()=>BadCatalog(c=>c.files[0].sha256="invalid");
        IEnumerator BadCatalog(Action<ConfigCatalog> mutate)=>UniTask.ToCoroutine(async()=>{var e=New();mutate(e.Catalog);Assert.IsFalse((await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()))).IsSuccess);Assert.IsFalse(e.Config.Get().IsSuccess);Assert.AreEqual(1,e.Provider.Keys.Count);Assert.IsTrue((await e.Pump(e.Context.CloseAsync(OperationContext.Unlimited()))).IsSuccess);Assert.AreEqual(0,e.Assets.RetainedEntryCount);});
        [UnityTest] public IEnumerator CorruptBytesNeverCommit()=>UniTask.ToCoroutine(async()=>{var e=New();e.CorruptData=true;Assert.IsFalse((await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()))).IsSuccess);Assert.IsFalse(e.Config.Get().IsSuccess);});
        [UnityTest] public IEnumerator DecoderExceptionRollsBackAllLeases()=>UniTask.ToCoroutine(async()=>{var e=New(decode:_=>throw new FormatException("decode"));Assert.IsFalse((await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()))).IsSuccess);Assert.IsTrue((await e.Pump(e.Context.CloseAsync(OperationContext.Unlimited()))).IsSuccess);Assert.AreEqual(0,e.Assets.RetainedEntryCount);});
        [UnityTest] public IEnumerator NullDecoderResultRollsBack()=>UniTask.ToCoroutine(async()=>{var e=New(decode:_=>null);Assert.IsFalse((await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()))).IsSuccess);Assert.IsFalse(e.Config.Get().IsSuccess);});
        [UnityTest] public IEnumerator ProviderFailureRollsBack()=>UniTask.ToCoroutine(async()=>{var e=New();e.FailLoad=true;Assert.IsFalse((await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()))).IsSuccess);Assert.AreEqual(0,e.Assets.RetainedEntryCount);});
        [UnityTest] public IEnumerator ReleaseFailureRetainsUntilExplicitContextRetry()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()));e.FailRelease=true;Assert.IsFalse((await e.Pump(e.Context.CloseAsync(OperationContext.Unlimited()))).IsSuccess);Assert.Greater(e.Assets.RetainedEntryCount,0);e.FailRelease=false;Assert.IsTrue((await e.Pump(e.Context.RetryCleanupAsync(Cleanup,OperationContext.Unlimited()))).IsSuccess);Assert.AreEqual(0,e.Assets.RetainedEntryCount);});
        [UnityTest] public IEnumerator RepeatedReadsDoNotReloadResources()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()));var original=e.Config.Get().Value;for(var i=0;i<1000;i++)Assert.AreSame(original,e.Config.Get().Value);Assert.AreEqual(3,e.Provider.Keys.Count);});
        [UnityTest] public IEnumerator StoppedPackagePreventsFurtherReads()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Pump(e.Context.StartAsync(OperationContext.Unlimited()));await e.Package.StopAsync(Cleanup);Assert.IsFalse(e.Config.Get().IsSuccess);});
        [Test] public void DefinitionRejectsDuplicateTables(){Assert.Throws<ArgumentException>(()=>new ConfigDefinition("p","main","v1","schema","catalog",new[]{"x","x"}));}
        [Test] public void DecoderRejectsTrailingBytes(){var files=new Dictionary<string,byte[]>{{"tblevel",Asset("tblevel").bytes.Concat(new byte[]{1}).ToArray()},{"tbreward",Asset("tbreward").bytes}};Assert.Throws<FormatException>(()=>GameConfigDecoder.Decode(files));}
    }
}
