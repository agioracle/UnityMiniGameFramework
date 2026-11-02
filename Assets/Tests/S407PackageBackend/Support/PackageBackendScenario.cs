using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Providers.YooAsset;
using UnityEngine;
using UnityEngine.Scripting;
using YooAsset;

namespace MiniFramework.S407PackageBackend
{
    [Serializable, Preserve] public sealed class PackageBackendCase
    {
        public string name, error="", observedError="", state="";
        public bool passed, loadedActualBundle, versionFrozen, foreignPreserved;
        public int consumers, leases, loadStarts, preparedAssets, modules, sessions, handles, entries, packages, bundlesBefore, bundlesAfter;
    }
    public sealed class PackageBackendScenario
    {
        public const string Package="S407RealBundle", Version="bundle-v1", Address="real-bundle", Text="S4-07 real AssetBundle / frozen content v1";
        public static readonly string[] Names={"ready_shared_leases","missing_first_play","config_mismatch","payload_mismatch","foreign_package_preserved"};
        public static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);
        public readonly List<PackageBackendCase> Cases=new List<PackageBackendCase>();
        readonly string root; readonly Func<InitializePackageOptions> options;
        public PackageBackendScenario(string root,Func<InitializePackageOptions> options){this.root=root;this.options=options;}
        public static void Require(bool v,string m){if(!v)throw new InvalidOperationException(m);}
        sealed class Consumer:IModule
        {
            readonly AssetsService assets;readonly PackageStartup startup;readonly PackageBackendCase row;
            public Consumer(AssetsService a,PackageStartup p,PackageBackendCase r){assets=a;startup=p;row=r;}
            public string Id=>"consumer";public IReadOnlyList<string> Dependencies=>new[]{"assets","package"};
            public async UniTask<Result> InitializeAsync(IModuleContext c,OperationContext w)
            {
                Require(startup.CheckReady(Package,Version).IsSuccess,"consumer.ready");row.consumers++;
                row.versionFrozen=!startup.CheckReady(Package,"other-version").IsSuccess;
                var scope=new Scope("package-backend-consumer");
                try
                {
                    var key=new ResourceKey("yooasset",Package,Version,Address,typeof(TextAsset));
                    var waits=new List<UniTask<Result<IAssetLease<TextAsset>>>>();for(int i=0;i<20;i++)waits.Add(assets.Load<TextAsset>(key,scope).Value.WaitAsync(w));
                    var ids=new HashSet<string>();foreach(var wait in waits){var r=await wait;Require(r.IsSuccess&&r.Value.Value.text.Trim()==Text,"consumer.payload");ids.Add(r.Value.Id);row.leases++;}
                    Require(ids.Count==20,"consumer.unique");
                }
                finally{Require((await scope.CloseAsync(Cleanup)).IsFullyDrained,"consumer.cleanup");}
                return Result.Success();
            }
            public UniTask<Result> StopAsync(CleanupContext c)=>UniTask.FromResult(Result.Success());
            public UniTask<Result> DisposeAsync(CleanupContext c)=>UniTask.FromResult(Result.Success());
        }
        static ModuleRegistration Reg(IModule m,params Type[] services)=>new ModuleRegistration(m.Id,m.Dependencies,()=>m,services);
        public async UniTask<PackageBackendCase> Run(string name)
        {
            Require(!YooAssets.TryGetPackage(Package,out _),"package.preexisting");
            var row=new PackageBackendCase{name=name,bundlesBefore=AssetBundle.GetAllLoadedAssetBundles().Count()};Cases.Add(row);
            ResourcePackage foreign=null;
            if(name==Names[4])foreign=YooAssets.CreatePackage(Package);
            var candidate=new PackageCandidate(PackageSource.Remote,Version,root);
            var address=name==Names[1]?"not-in-bundle":Address;
            var definition=new YooAssetPackageDefinition(Package,candidate,name==Names[2]?"other-version":Version,"bundle-schema-v1",new[]{"fixture-app-v1"},new[]{new YooAssetFirstPlayAsset(address,typeof(TextAsset),asset=>{
                row.loadedActualBundle=AssetBundle.GetAllLoadedAssetBundles().Count()>row.bundlesBefore;
                return ((TextAsset)asset).text.Trim()==(name==Names[3]?"wrong-payload":Text)?Result.Success():Result.Failure(new FrameworkError(ErrorKind.Integrity,"fixture.package.payload"));
            })},options);
            var backend=new YooAssetPackageBackend(new[]{definition});
            var startup=new PackageStartup(new PackageProfile(Package,"fixture-app-v1","bundle-schema-v1",new[]{address},new[]{candidate}),backend);
            var provider=new YooAssetPackageProvider(backend,startup);
            var assets=new AssetsService(provider,Package,Version,Cleanup,providerModuleId:provider.Id,packageReadiness:startup,packageModuleId:startup.Id);
            var consumer=new Consumer(assets,startup,row);
            var context=new GameContext(new[]{Reg(startup,typeof(IPackageReadiness)),Reg(provider,typeof(IAssetProvider),typeof(IAssetDownloadProvider)),Reg(assets,typeof(IAssetsService)),Reg(consumer)},Cleanup);
            try
            {
                var result=await context.StartAsync(new OperationContext(default,TimeSpan.FromSeconds(60)));
                row.observedError=result.IsSuccess?"":result.Error.Code;row.state=startup.State.ToString();row.preparedAssets=backend.PreparedAssetCount;
                var expected=name==Names[1]?"yoo.asset.missing":name==Names[2]?"package.manifest.compatibility":name==Names[3]?"fixture.package.payload":name==Names[4]?"yoo.package.foreign":"";
                Require(row.observedError==expected,"case.result:"+row.observedError);
                Require(row.consumers==(expected==""?1:0),"consumer.gate");
                if(expected=="")Require(row.loadedActualBundle&&row.leases==20&&row.versionFrozen&&provider.LoadStartCount==1,"business.load");
                if(foreign!=null){row.foreignPreserved=YooAssets.TryGetPackage(Package,out var found)&&ReferenceEquals(found,foreign);Require(row.foreignPreserved,"foreign.lost");}
            }
            catch(Exception e){row.error=e.Message;}
            finally
            {
                try{Require((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess,"context.close");}catch(Exception e){row.error+=";"+e.Message;}
                row.modules=context.RetainedModuleCount;row.sessions=backend.RetainedSessionCount;row.handles=provider.RetainedHandleCount;row.entries=assets.RetainedEntryCount;row.loadStarts=provider.LoadStartCount;
                if(foreign!=null)YooAssets.RemovePackage(Package); // Only this fixture created the uninitialized foreign package.
                row.packages=YooAssets.TryGetPackage(Package,out _)?1:0;row.bundlesAfter=AssetBundle.GetAllLoadedAssetBundles().Count();
                row.passed=row.error==""&&row.modules+row.sessions+row.handles+row.entries+row.packages==0&&row.bundlesBefore==row.bundlesAfter;
            }
            return row;
        }
        public async UniTask Execute(){foreach(var n in Names)Require((await Run(n)).passed,"case.failed:"+n);}
    }
}
