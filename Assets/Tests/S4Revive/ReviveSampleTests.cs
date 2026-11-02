using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Access;
using Game.Boot;
using MiniFramework.Contracts;
using MiniFramework.Host;
using MiniFramework.Providers.StorageFake;
using MiniFramework.Ads;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ReviveTests;
public sealed class ReviveSampleTests
{
    GameObject host,root;LocalSampleGame game;ReviveSampleOptions options;FakeStorageStore disk;Scene original;bool failLoad;
    [UnitySetUp] public IEnumerator Setup()=>UniTask.ToCoroutine(async()=>{original=SceneManager.GetActiveScene();host=new GameObject("ReviveTestHost",typeof(RuntimeHost));root=new GameObject("ReviveTestUi",typeof(RectTransform));disk=new FakeStorageStore();options=new ReviveSampleOptions(new FakeStorageProvider(disk));game=new LocalSampleGame(host.GetComponent<RuntimeHost>(),root.transform,path=>failLoad?null:SceneManager.LoadSceneAsync(path,LoadSceneMode.Additive),revive:options);Env.Check(await game.StartAsync());});
    [UnityTearDown] public IEnumerator Teardown()=>UniTask.ToCoroutine(async()=>{failLoad=false;options.Ads.FailCleanup=false;foreach(var a in options.Ads.Attempts){a.CompleteLoad();a.CompleteShow();}var close=await game.CloseAsync();if(!close.IsSuccess)close=await game.Context.RetryCleanupAsync(LocalSampleGame.Cleanup,LocalSampleGame.Wait);Env.Check(close);if(original.IsValid()&&original.isLoaded)SceneManager.SetActiveScene(original);UnityEngine.Object.Destroy(host);UnityEngine.Object.Destroy(root);await UniTask.Yield();});
    async UniTask Level(){Env.Check(game.RequestLevel(1));Env.Check(await game.Pending);}
    [UnityTest] public IEnumerator NewSampleMountsContractsAndAuthoredOverlay()=>UniTask.ToCoroutine(async()=>{Assert.IsTrue(game.Context.TryGet<IReviveService>(out var service));Assert.AreSame(game.Revive,service);Assert.IsTrue(game.Context.TryGet<IClaimOwnerAuthority>(out _));Assert.IsFalse(game.Context.TryGet<IAccountService>(out _));Assert.NotNull(game.ReviveView.Panel.State);StringAssert.Contains("MOCK",game.ReviveView.Panel.State.text);StringAssert.Contains("Volatile",game.ReviveView.Panel.State.text);Assert.AreEqual(7,game.PackageBackend.PreparedResourceCount);await UniTask.Yield();});
    [UnityTest] public IEnumerator AuthoredButtonsFailWatchAndProjectCommittedProgress()=>UniTask.ToCoroutine(async()=>{await Level();game.ReviveView.Panel.FailButton.onClick.Invoke();Env.Check(await game.Pending);Assert.AreEqual(ReviveRunPhase.Failed,game.Revive.Snapshot.Phase);var tick=game.GameplayTicks;await Env.Frames();Assert.AreEqual(tick,game.GameplayTicks);game.ReviveView.Panel.WatchButton.onClick.Invoke();Env.Check(await game.Pending);Assert.AreEqual(5,game.Coins);Assert.IsTrue(game.Revive.Snapshot.ReviveUsed);Assert.AreEqual(ReviveRunPhase.Playing,game.Revive.Snapshot.Phase);await Env.Frames();Assert.Greater(game.GameplayTicks,tick);game.RefreshReviveStatus();StringAssert.Contains("Coins 5",game.ReviveView.Panel.State.text);});
    [UnityTest] public IEnumerator ReopenSameProviderRestoresLevelRewardAndGeneration()=>UniTask.ToCoroutine(async()=>{await Level();Env.Check(game.RequestFailure());Env.Check(await game.Pending);Env.Check(game.RequestWatch(default));Env.Check(await game.Pending);var run=game.Revive.Snapshot.RunId;var generation=game.Revive.Snapshot.LastGeneration;Env.Check(await game.CloseAsync());game=new LocalSampleGame(host.GetComponent<RuntimeHost>(),root.transform,revive:new ReviveSampleOptions(new FakeStorageProvider(disk),options.Facts));Env.Check(await game.StartAsync());Assert.AreEqual(5,game.Coins);Assert.AreEqual(run,game.Revive.Snapshot.RunId);Assert.Greater(game.Revive.Snapshot.LastGeneration,generation);Assert.NotNull(game.Level);Assert.IsFalse(game.Revive.CanRevive);});
    [UnityTest] public IEnumerator ClosedViewCannotReceiveNewSuccessOrRebindOldButton()=>UniTask.ToCoroutine(async()=>{await Level();Env.Check(game.RequestFailure());Env.Check(await game.Pending);options.Ads.HoldShow=true;var old=game.ReviveView;var oldPanel=old.Panel;oldPanel.WatchButton.onClick.Invoke();var wait=game.Pending;await Env.Frames();Env.Check(await old.CloseAsync(LocalSampleGame.Wait));Assert.AreEqual(ErrorKind.Canceled,(await wait).Error.Kind);Assert.AreEqual(0,options.Ads.Attempts[0].CleanupCount);options.Ads.Attempts[0].CompleteShow();await Env.Frames(10);Assert.AreEqual(5,game.Coins);Assert.AreEqual(1,game.UI.SessionCount);oldPanel.WatchButton.onClick.Invoke();Assert.AreEqual(1,options.Ads.Attempts.Count);});
    [UnityTest] public IEnumerator PendingClaimBlocksNewRunUntilExplicitResolution()=>UniTask.ToCoroutine(async()=>{await Level();Env.Check(game.RequestFailure());Env.Check(await game.Pending);options.Ads.HoldShow=true;Env.Check(game.RequestWatch(default));var wait=game.Pending;disk.Faults.Enqueue(FakeStorageFault.Reject);options.Ads.Attempts[0].CompleteShow();Assert.IsFalse((await wait).IsSuccess);Env.Check(game.RequestLevel(2));Assert.IsFalse((await game.Pending).IsSuccess);Assert.AreEqual("level/1",game.Flow.Current.Id);Env.Check(game.RequestRecoverClaim());Env.Check(await game.Pending);Assert.AreEqual(5,game.Coins);Assert.AreEqual(1,options.Ads.Attempts.Count);});
    [UnityTest] public IEnumerator TwoLevelRegularRewardsAndReviveAreSingleProjection()=>UniTask.ToCoroutine(async()=>{await Level();Env.Check(game.Collect());Env.Check(await game.Pending);Assert.AreEqual(10,game.Coins);Assert.IsTrue(game.Collect().IsSuccess);Assert.IsFalse((await game.Pending).IsSuccess);Env.Check(game.RequestFailure());Env.Check(await game.Pending);Env.Check(game.RequestWatch(default));Env.Check(await game.Pending);Env.Check(game.RequestLevel(2));Env.Check(await game.Pending);Env.Check(game.Collect());Env.Check(await game.Pending);Assert.AreEqual(40,game.Coins);Assert.AreEqual(2,game.Revive.Snapshot.HighestCollectedLevel);});
    [UnityTest] public IEnumerator RealBootstrapSceneRunsOnlyOneCompositionAndReopens()=>UniTask.ToCoroutine(async()=>
    {
        Env.Check(await game.CloseAsync());var load=SceneManager.LoadSceneAsync(ReviveSampleOptions.Scene,LoadSceneMode.Additive);while(!load.isDone)await UniTask.Yield();var scene=SceneManager.GetSceneByPath(ReviveSampleOptions.Scene);ReviveSampleBootstrap boot=null;
        try
        {
            foreach(var go in scene.GetRootGameObjects()){var b=go.GetComponentInChildren<ReviveSampleBootstrap>(true);if(b)boot=b;}Assert.NotNull(boot);
            for(var i=0;i<600&&!boot.Ready;i++)await UniTask.Yield();Assert.IsTrue(boot.Ready,boot.Status.text);Assert.IsFalse(boot.GetComponent<LocalSampleBootstrap>().enabled);
            var g=boot.Game;Assert.IsFalse((await boot.OpenAsync(retryCleanup:true)).IsSuccess);Assert.AreSame(g,boot.Game);Assert.AreEqual(GameContextState.Ready,g.Context.State);Assert.AreEqual(ScopeState.Open,g.App.State);Env.Check(g.RequestLevel(1));Env.Check(await g.Pending);Env.Check(g.RequestFailure());Env.Check(await g.Pending);g.ReviveView.Panel.WatchButton.onClick.Invoke();Env.Check(await g.Pending);Assert.AreEqual(5,g.Coins);Env.Check(await boot.OpenAsync());Assert.AreEqual(5,boot.Game.Coins);Assert.AreEqual(0,g.Context.RetainedModuleCount);Assert.IsTrue(boot.Game.Revive.Snapshot.ReviveUsed);
            boot.VolatileStore.FailCleanup=true;Assert.IsFalse((await boot.OpenAsync()).IsSuccess);Assert.AreEqual(GameContextState.CleanupFailed,boot.Game.Context.State);boot.VolatileStore.FailCleanup=false;Env.Check(await boot.OpenAsync(retryCleanup:true));Assert.AreEqual(5,boot.Game.Coins);Env.Check(await boot.StopAsync());
        }
        finally{if(boot)await boot.StopAsync();var unload=SceneManager.UnloadSceneAsync(scene);while(unload!=null&&!unload.isDone)await UniTask.Yield();}
    });
    [UnityTest] public IEnumerator FailedSceneSelectionDoesNotAdvancePersistentRun()=>UniTask.ToCoroutine(async()=>
    {
        var before=game.Revive.Snapshot;failLoad=true;Env.Check(game.RequestLevel(1));Assert.IsFalse((await game.Pending).IsSuccess);Assert.AreSame(before,game.Revive.Snapshot);Assert.NotNull(game.Home);Assert.AreEqual(ReviveRunPhase.NoRun,game.Revive.Snapshot.Phase);failLoad=false;await Level();Assert.AreEqual(1,game.Revive.Snapshot.Level);
    });
    [UnityTest] public IEnumerator RejectedRunCommitGatesSelectedSceneUntilLevelReopened()=>UniTask.ToCoroutine(async()=>
    {
        await Level();var run=game.Revive.Snapshot.RunId;disk.Faults.Enqueue(FakeStorageFault.Reject);Env.Check(game.RequestLevel(2));Assert.IsFalse((await game.Pending).IsSuccess);Assert.AreEqual("level/2",game.Flow.Current.Id);Assert.AreEqual(run,game.Revive.Snapshot.RunId);Assert.AreEqual(1,game.Revive.Snapshot.Level);var ticks=game.GameplayTicks;await Env.Frames();Assert.AreEqual(ticks,game.GameplayTicks);Assert.IsFalse(game.Collect().IsSuccess);Assert.IsFalse(game.RequestFailure().IsSuccess);Assert.IsFalse(game.RequestWatch(default).IsSuccess);Env.Check(game.RequestHome());Env.Check(await game.Pending);Env.Check(game.RequestLevel(2));Env.Check(await game.Pending);Assert.AreEqual(2,game.Revive.Snapshot.Level);Assert.AreNotEqual(run,game.Revive.Snapshot.RunId);
    });
    [UnityTest] public IEnumerator CloseReturnsAllRuntimeOwnershipToZero()=>UniTask.ToCoroutine(async()=>{await Level();Env.Check(game.RequestFailure());Env.Check(await game.Pending);Env.Check(game.RequestWatch(default));Env.Check(await game.Pending);Env.Check(await game.CloseAsync());Assert.AreEqual(0,game.Context.RetainedModuleCount);Assert.AreEqual(0,game.Provider.RetainedHandleCount);Assert.AreEqual(0,game.SceneProvider.RetainedHandleCount);Assert.IsFalse(game.Revive.Busy);});
}
