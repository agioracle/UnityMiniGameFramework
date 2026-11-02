using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Boot;
using Game.Features;
using MiniFramework.Contracts;
using MiniFramework.Host;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
public sealed class TemplatePlayTests
{
    GameObject host,root;LocalSampleGame game;Scene original;bool failLoad,failUnload;
    [UnitySetUp] public IEnumerator Setup()=>UniTask.ToCoroutine(async()=>
    {
        original=SceneManager.GetActiveScene();host=new GameObject("TemplateTestHost",typeof(RuntimeHost));root=new GameObject("TemplateTestLayer",typeof(RectTransform));
        game=new LocalSampleGame(host.GetComponent<RuntimeHost>(),root.transform,path=>failLoad?null:SceneManager.LoadSceneAsync(path,LoadSceneMode.Additive),scene=>failUnload?null:SceneManager.UnloadSceneAsync(scene));
        Check(await game.StartAsync());
    });
    [UnityTearDown] public IEnumerator Cleanup()=>UniTask.ToCoroutine(async()=>{failLoad=failUnload=false;if(game!=null){Check(await game.CloseAsync());Assert.AreEqual(0,game.Provider.RetainedHandleCount);Assert.AreEqual(0,game.SceneProvider.RetainedHandleCount);Assert.AreEqual(0,game.Context.RetainedModuleCount);}if(original.IsValid()&&original.isLoaded)SceneManager.SetActiveScene(original);UnityEngine.Object.Destroy(host);UnityEngine.Object.Destroy(root);await UniTask.Yield();});
    static void Check(Result r)=>Assert.IsTrue(r.IsSuccess,r.IsSuccess?"":r.Error.ToString());
    async UniTask Enter(int id){Check(game.RequestLevel(id));Check(await game.Pending);}
    async UniTask Home(){Check(game.RequestHome());Check(await game.Pending);}
    [UnityTest] public IEnumerator StartsWithRealConfigAndBoundHome()=>UniTask.ToCoroutine(async()=>{Assert.NotNull(game.Home.Panel);StringAssert.Contains("Platform: Mock",game.Home.Panel.Status.text);Assert.AreEqual(10,game.Config.Get().Value.TbReward.Get(1).Coins);Assert.AreEqual(6,game.PackageBackend.PreparedResourceCount);Assert.AreEqual(0,game.SceneProvider.RetainedHandleCount);await UniTask.Yield();});
    [UnityTest] public IEnumerator ConfigDrivenTwoLevelsAndAudio()=>UniTask.ToCoroutine(async()=>{await Enter(1);Check(game.Collect());Assert.AreEqual(10,game.Coins);Check(await game.SoundCompletion);Assert.GreaterOrEqual(game.AudioBackend.SourceCount,1);await Home();await Enter(2);Check(game.Collect());Assert.AreEqual(35,game.Coins);Check(game.ToggleMute());Assert.IsTrue(game.Muted);Check(game.ToggleMute());Assert.IsFalse(game.Muted);});
    [UnityTest] public IEnumerator ConcurrentTenOpenUsesOneSession()=>UniTask.ToCoroutine(async()=>{var tasks=new List<UniTask<Result<IUiSession<SampleHomePanel>>>>();for(var i=0;i<10;i++)tasks.Add(game.Access.OpenAsync<SampleHomePanel>(game.CurrentScope,LocalSampleGame.Wait));var first=game.Home.Id;foreach(var task in tasks){var r=await task;Assert.IsTrue(r.IsSuccess);Assert.AreEqual(first,r.Value.Id);}Assert.AreEqual(1,game.UI.SessionCount);});
    [UnityTest] public IEnumerator FiftyCyclesReturnToWarmBaseline()=>UniTask.ToCoroutine(async()=>
    {
        await Enter(1);await Home();var baseline=Snapshot();
        for(var i=0;i<50;i++){await Enter(i%2+1);var previous=game.CurrentScope;var oldView=game.Level.ViewScope;await Home();Assert.AreEqual(ScopeState.Closed,previous.State);Assert.AreEqual(ScopeState.Closed,oldView.State);CollectionAssert.AreEqual(baseline,Snapshot(),"cycle "+i);}
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath,"../Library/S306TemplateCycles.json"),JsonUtility.ToJson(new CycleEvidence{cycles=50,baseline=baseline,final=Snapshot(),created=game.Pool.CreatedCount},true));
        Assert.AreEqual(2,game.Pool.CreatedCount);Assert.AreEqual(1,game.UI.SessionCount);Assert.AreEqual(0,game.Input.ListenerCount);
    });
    [Serializable] sealed class CycleEvidence{public int cycles,created;public int[] baseline,final;}
    int[] Snapshot()=>new[]{game.Pool.ActiveCount,game.Pool.RetainedInstanceCount,game.Pool.BucketCount,game.UI.SessionCount,game.UI.CachedCount,game.Provider.RetainedHandleCount,game.SceneProvider.RetainedHandleCount,game.Input.ListenerCount,game.Home.ViewScope.Cleanup.Snapshot.PendingCount,game.Context.ReadDiagnostics().ActiveScopes,game.Flow.ReadDiagnostics().ActiveScopes,game.UI.ReadDiagnostics().ActiveScopes,game.UI.ReadDiagnostics().PendingOperations,game.UI.ReadDiagnostics().FailedCleanup};
    [UnityTest] public IEnumerator HundredCachedOpenCloseKeepsOneClickListener()=>UniTask.ToCoroutine(async()=>
    {
        var panel=game.Home.Panel;
        for(var i=0;i<100;i++){Check(await game.Home.CloseAsync(LocalSampleGame.Wait));var opened=await game.Access.OpenAsync<SampleHomePanel>(game.CurrentScope,LocalSampleGame.Wait);Assert.IsTrue(opened.IsSuccess);Assert.AreSame(panel,opened.Value.Panel);opened.Value.Panel.Configure(game,"Home",false);}
        var before=panel.ClickCount;panel.StartButton.onClick.Invoke();Check(await game.Pending);Assert.AreEqual(before+1,panel.ClickCount);Assert.AreEqual(2,game.TransitionCount);
    });
    [UnityTest] public IEnumerator CloseDuringOpeningDrainsAndReopens()=>UniTask.ToCoroutine(async()=>{await game.Home.CloseAsync(LocalSampleGame.Wait);var owner=game.CurrentScope;var opening=game.Access.OpenAsync<SampleLevelPanel>(owner,LocalSampleGame.Wait);var close=game.UI.CloseAsync<SampleLevelPanel>(owner,LocalSampleGame.Wait);await opening;Check(await close);Assert.AreEqual(0,game.UI.SessionCount);var reopened=await game.Access.OpenAsync<SampleHomePanel>(owner,LocalSampleGame.Wait);Assert.IsTrue(reopened.IsSuccess);});
    [UnityTest] public IEnumerator FailedLoadPreservesHomeAndRetryWorks()=>UniTask.ToCoroutine(async()=>{var before=game.Home;failLoad=true;Check(game.RequestLevel(1));Assert.IsFalse((await game.Pending).IsSuccess);Assert.AreEqual(before.Id,game.Home.Id);Assert.AreEqual(SessionFlowState.Ready,game.Flow.State);failLoad=false;await Enter(1);});
    [UnityTest] public IEnumerator FailedUnloadStopsOldGameplayAndExplicitRetry()=>UniTask.ToCoroutine(async()=>{await Enter(1);var actor=UnityEngine.Object.FindObjectOfType<SampleLevelActor>();await UniTask.Yield();var oldScope=game.CurrentScope;failUnload=true;Check(game.RequestHome());Assert.IsFalse((await game.Pending).IsSuccess);Assert.AreEqual(SessionFlowState.Recovering,game.Flow.State);Assert.AreEqual(ScopeState.CleanupFailed,oldScope.State);var ticks=actor.TickCount;for(var i=0;i<4;i++)await UniTask.Yield();Assert.AreEqual(ticks,actor.TickCount);Assert.IsTrue(game.Home.Panel.RetryButton.gameObject.activeSelf);failUnload=false;Check(game.RequestRetry());Check(await game.Pending);Assert.AreEqual(SessionFlowState.Ready,game.Flow.State);Assert.AreEqual(0,game.SceneProvider.RetainedHandleCount);Assert.AreEqual(ScopeState.Closed,oldScope.State);
        // A committed level-to-level failure renders a recovery Home inside the new Session.
        // Recovery must close that Home before restoring the level UI and input.
        await Enter(1);failUnload=true;Check(game.RequestLevel(2));Assert.IsFalse((await game.Pending).IsSuccess);Assert.NotNull(game.Home);Assert.AreEqual(SessionFlowState.Recovering,game.Flow.State);
        failUnload=false;Check(game.RequestRetry());Check(await game.Pending);Assert.AreEqual(1,game.UI.SessionCount);Assert.IsNull(game.Home);Assert.NotNull(game.Level);Assert.AreEqual(1,game.Input.ListenerCount);Assert.AreEqual("level/2",game.Flow.Current.Id);await Home();
    });
    [UnityTest] public IEnumerator BootstrapSceneSurvivesLevelTransition()=>UniTask.ToCoroutine(async()=>{await Enter(1);Assert.IsTrue(original.IsValid()&&original.isLoaded);Assert.AreEqual(SampleContent.TutorialScene,SceneManager.GetActiveScene().path);await Home();Assert.IsTrue(original.IsValid()&&original.isLoaded);});
    [UnityTest] public IEnumerator BusyCommandRejectedWithoutDuplicateTransition()=>UniTask.ToCoroutine(async()=>{Check(game.RequestLevel(1));Assert.IsFalse(game.RequestLevel(2).IsSuccess);Check(await game.Pending);Assert.AreEqual("level/1",game.Flow.Current.Id);});
    [UnityTest] public IEnumerator ClosedContextRejectsCommands()=>UniTask.ToCoroutine(async()=>{Check(await game.CloseAsync());Assert.IsFalse(game.RequestHome().IsSuccess);});
}
