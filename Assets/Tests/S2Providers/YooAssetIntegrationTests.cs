#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Providers.YooAsset;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;
using YooAsset.Editor;

namespace MiniFramework.Providers.Tests
{
    public sealed class YooAssetIntegrationTests
    {
        private ResourcePackage package;
        private YooAssetProvider provider;
        private bool ownSdk;
        private static CleanupContext Cleanup => new CleanupContext(CancellationToken.None, Timeout.InfiniteTimeSpan);
        private ResourceKey Key(string address="probe", string version="s203-v1") => new ResourceKey("yooasset",package.PackageName,version,address,typeof(TextAsset));
        [UnitySetUp] public IEnumerator SetUp() => UniTask.ToCoroutine(async()=>
        {
            ownSdk=!YooAssets.IsInitialized;
            if(ownSdk)YooAssets.Initialize();
            var name="S203_"+Guid.NewGuid().ToString("N");
            var existed=BundleCollectorSettingData.HasSettingAsset();
            var setting=BundleCollectorSettingData.Setting;
            var collector=new BundleCollector { CollectPath=ProviderAdapterTests.Fixture, CollectorGUID=AssetDatabase.AssetPathToGUID(ProviderAdapterTests.Fixture), PackRuleName=nameof(PackSeparately) };
            var group=new BundleCollectorGroup { GroupName="Fixture" }; group.Collectors.Add(collector);
            var spec=new BundleCollectorPackage { PackageName=name,EnableAddressable=true,AutoCollectShaders=false };spec.Groups.Add(group);
            setting.Packages.Add(spec);
            string root;
            try
            {
                var parameters=new EditorSimulateBuildParameters {
                    BuildOutputRoot=Path.GetFullPath("Library/S2ProviderBundles"), BundledFileRoot=Path.GetFullPath("Library/S2ProviderBundled"),
                    BuildPipeline=EBuildPipeline.EditorSimulateBuildPipeline.ToString(), BuildBundleType=(int)EBundleType.VirtualAssetBundle,
                    BuildTarget=EditorUserBuildSettings.activeBuildTarget, PackageName=name, PackageVersion="s203-v1", ClearBuildCacheFiles=true
                };
                var result=new EditorSimulateBuildPipeline().Run(parameters,false);
                Assert.IsTrue(result.Success,result.ErrorInfo);root=result.OutputPackageDirectory;
            }
            finally
            {
                setting.Packages.Remove(spec);
                // Do not save or alter a pre-existing user's collector configuration.
                if(!existed)AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(setting));
            }
            package=YooAssets.CreatePackage(name);
            var options=new EditorSimulateModeOptions { EditorFileSystemParameters=FileSystemParameters.CreateDefaultEditorFileSystemParameters(root) };
            var initialize=package.InitializePackageAsync(options);
            while(!initialize.IsDone)await UniTask.Yield();
            Assert.AreEqual(EOperationStatus.Succeeded,initialize.Status,initialize.Error);
            var manifest=package.LoadPackageManifestAsync(new LoadPackageManifestOptions("s203-v1",60));
            while(!manifest.IsDone)await UniTask.Yield();
            Assert.AreEqual(EOperationStatus.Succeeded,manifest.Status,manifest.Error);
            provider=new YooAssetProvider(package,"s203-v1");
        });
        [UnityTearDown] public IEnumerator TearDown() => UniTask.ToCoroutine(async()=>
        {
            if(package!=null)
            {
                var destroy=package.DestroyPackageAsync();while(!destroy.IsDone)await UniTask.Yield();
                Assert.AreEqual(EOperationStatus.Succeeded,destroy.Status,destroy.Error);
                YooAssets.RemovePackage(package.PackageName);package=null;
            }
            if(ownSdk)YooAssets.Destroy();
            await UniTask.Yield();
        });
        private async UniTask<GameContext> StartService(AssetsService service)
        {
            var c=new GameContext(new[]{new ModuleRegistration(provider.Id,provider.Dependencies,()=>provider,typeof(IAssetProvider)),new ModuleRegistration(service.Id,service.Dependencies,()=>service,typeof(IAssetsService))},Cleanup);
            Assert.IsTrue((await c.StartAsync(OperationContext.Unlimited())).IsSuccess);return c;
        }
        [UnityTest] public IEnumerator SdkLoadAndReleaseHandleThenConfirmUnusedSweep() => UniTask.ToCoroutine(async()=>
        {
            var op=provider.BeginLoad<TextAsset>(Key()).Value;
            var result=await op.Completion;Assert.IsTrue(result.IsSuccess);var asset=result.Value;
            Assert.AreEqual("S2-03 local provider fixture",asset.Value.text.Trim());Assert.AreEqual(1,provider.RetainedHandleCount);
            Assert.IsTrue((await asset.BeginRelease(Cleanup).Value.Completion).IsSuccess);Assert.AreEqual(0,provider.RetainedHandleCount);
            var sweep=package.UnloadUnusedAssetsAsync();while(!sweep.IsDone)await UniTask.Yield();Assert.AreEqual(EOperationStatus.Succeeded,sweep.Status,sweep.Error);
        });
        [UnityTest] public IEnumerator SdkTwentyCallersShareOneHandleAndContextDrainsInOrder() => UniTask.ToCoroutine(async()=>
        {
            var service=new AssetsService(provider,package.PackageName,"s203-v1",Cleanup,providerModuleId:provider.Id);var context=await StartService(service);
            var owner=new Scope("sdk-shared");var waits=new List<UniTask<Result<IAssetLease<TextAsset>>>>();
            for(var i=0;i<20;i++)waits.Add(service.Load<TextAsset>(Key(),owner).Value.WaitAsync(OperationContext.Unlimited()));
            var leases=new List<IAssetLease<TextAsset>>();foreach(var wait in waits)leases.Add((await wait).Value);
            Assert.AreEqual(1,provider.LoadStartCount);Assert.AreEqual(20,new HashSet<IAssetLease<TextAsset>>(leases).Count);
            for(var i=0;i<19;i++)Assert.IsTrue((await leases[i].ReleaseAsync(Cleanup)).IsSuccess);
            Assert.AreEqual(1,provider.RetainedHandleCount);
            Assert.IsTrue((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);Assert.AreEqual(0,provider.RetainedHandleCount);Assert.AreEqual(0,service.RetainedEntryCount);
            Assert.IsTrue((await owner.CloseAsync(Cleanup)).IsFullyDrained);
        });
        [UnityTest] public IEnumerator SdkOwnerClosesBeforeDeliveryAndLateAssetIsReclaimed() => UniTask.ToCoroutine(async()=>
        {
            var service=new AssetsService(provider,package.PackageName,"s203-v1",Cleanup,providerModuleId:provider.Id);var context=await StartService(service);var owner=new Scope("sdk-late");
            var wait=service.Load<TextAsset>(Key(),owner).Value.WaitAsync(OperationContext.Unlimited());var close=owner.CloseAsync(Cleanup);
            Assert.AreEqual(ErrorKind.Canceled,(await wait).Error.Kind);Assert.IsTrue((await close).IsFullyDrained);Assert.AreEqual(0,provider.RetainedHandleCount);Assert.AreEqual(0,service.ActualLoadingCount);
            Assert.IsTrue((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);
        });
        [Test] public void SdkMissingAddressAndSelectedVersionMismatchAreTypedFailures()
        {
            Assert.AreEqual(ErrorKind.Unavailable,provider.BeginLoad<TextAsset>(Key("missing")).Error.Kind);
            var wrong=new YooAssetProvider(package,"wrong-version");Assert.AreEqual(ErrorKind.Integrity,wrong.BeginLoad<TextAsset>(Key(version:"wrong-version")).Error.Kind);Assert.AreEqual(0,provider.LoadStartCount);
        }
        [UnityTest] public IEnumerator SdkScheduledDownloadThenPreparedLoadSharesSinglePipeline() => UniTask.ToCoroutine(async()=>
        {
            var scheduler=new ResourceWorkScheduler(new ResourceScheduleBudget(1,1,2,TimeSpan.FromMilliseconds(2)));
            var service=new AssetsService(provider,package.PackageName,"s203-v1",Cleanup,providerModuleId:provider.Id,scheduler:scheduler,schedulerModuleId:scheduler.Id);
            var context=new GameContext(new[]{new ModuleRegistration(provider.Id,provider.Dependencies,()=>provider,typeof(IAssetProvider)),new ModuleRegistration(scheduler.Id,scheduler.Dependencies,()=>scheduler,typeof(IResourceWorkScheduler)),new ModuleRegistration(service.Id,service.Dependencies,()=>service,typeof(IAssetsService))},Cleanup);
            Assert.IsTrue((await context.StartAsync(OperationContext.Unlimited())).IsSuccess);
            var owner=new Scope("sdk-scheduled");var waits=new List<UniTask<Result<IAssetLease<TextAsset>>>>();
            for(var i=0;i<20;i++)waits.Add(service.Load<TextAsset>(Key(),owner,ResourcePriority.Critical).Value.WaitAsync(OperationContext.Unlimited()));
            Assert.AreEqual(0,provider.DownloadStartCount);Assert.AreEqual(1,scheduler.QueuedDownloads);
            while(waits[0].Status==UniTaskStatus.Pending){scheduler.Tick();await UniTask.NextFrame();}
            foreach(var wait in waits)Assert.IsTrue((await wait).IsSuccess);
            Assert.AreEqual(1,provider.DownloadStartCount);Assert.AreEqual(1,provider.LoadStartCount);Assert.AreEqual(0,scheduler.ActiveDownloads+scheduler.ActiveLoads);
            Assert.IsTrue((await owner.CloseAsync(Cleanup)).IsFullyDrained);Assert.IsTrue((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);Assert.AreEqual(0,provider.RetainedHandleCount);
        });
        [UnityTest] public IEnumerator SdkTenServiceCyclesLeaveNoProviderHandles() => UniTask.ToCoroutine(async()=>
        {
            var service=new AssetsService(provider,package.PackageName,"s203-v1",Cleanup,providerModuleId:provider.Id);var context=await StartService(service);
            for(var i=0;i<10;i++) {var owner=new Scope("cycle-"+i);var asset=(await service.Load<TextAsset>(Key(),owner).Value.WaitAsync(OperationContext.Unlimited())).Value;Assert.IsNotNull(asset.Value);Assert.IsTrue((await owner.CloseAsync(Cleanup)).IsFullyDrained);Assert.AreEqual(0,provider.RetainedHandleCount);Assert.AreEqual(0,service.RetainedEntryCount);}
            Assert.AreEqual(10,provider.LoadStartCount);Assert.IsTrue((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);
        });
    }
}
#endif
