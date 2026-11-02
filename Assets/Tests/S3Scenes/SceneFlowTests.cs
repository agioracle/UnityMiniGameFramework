using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Access;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Scenes;
using MiniFramework.Providers.Fake;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
public sealed class SceneFlowTests
{
    static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);
    static OperationContext Wait=>OperationContext.Unlimited();
    static ResourceKey Key(string address="a",string provider="fake",string version="v1")=>ResourceKey.Scene(provider,"main",version,address);
    static SceneLoadOptions Add(bool active=true)=>new SceneLoadOptions(SceneLoadMode.Additive,active);
    static SceneLoadOptions Single(bool active=true)=>new SceneLoadOptions(SceneLoadMode.Single,active);
    sealed class Env
    {
        internal readonly FakeResourceProvider Fake;
        internal readonly SceneService Scenes;internal readonly SessionFlow Flow;internal readonly GameContext Context;
        internal readonly List<Scope> Owners=new List<Scope>();
        internal bool AutoLoad=true,AutoActivate=true,AutoUnload=true,FailUnload,FailLoad,FailActivate;
        internal Env(bool deferred=true,bool noScenes=false,UnitySceneProvider native=null,IPackageReadiness readiness=null)
        {
            Fake=new FakeResourceProvider(supportsDeferredActivation:deferred);
            Scenes=new SceneService(native==null?(ISceneProvider)Fake:native,native==null?"fake":"native","main","v1",Cleanup,readiness);
            Flow=new SessionFlow(Cleanup,noScenes?null:Scenes);
            var modules=new List<ModuleRegistration>{new ModuleRegistration(Flow.Id,Flow.Dependencies,()=>Flow,typeof(ISessionFlow))};
            if(!noScenes)modules.Add(new ModuleRegistration(Scenes.Id,Scenes.Dependencies,()=>Scenes,typeof(ISceneService)));
            Context=new GameContext(modules,Cleanup);
        }
        internal Scope Owner(){var owner=new Scope(Guid.NewGuid().ToString("N"),ScopeKind.Session);Owners.Add(owner);return owner;}
        internal Result<IOwnedOperation<ISceneLease>> Load(Scope owner=null,SceneLoadOptions? options=null,ResourceKey? key=null)=>Scenes.Load(key??Key(),options??Add(),owner??Owner());
        internal void Step()
        {
            for(var i=0;i<Fake.SceneStartCount;i++)
            {
                var load=Fake.SceneAt(i);
                if(AutoLoad&&load.Operation.State==BackendState.Running){if(FailLoad)load.Fail(new FrameworkError(ErrorKind.Provider,"test.load"));else load.Succeed();}
                var scene=load.Scene;if(scene==null)continue;
                if(AutoActivate&&scene.Activation?.State==BackendState.Running)scene.Activation.Complete(FailActivate?Result<Unit>.Failure(new FrameworkError(ErrorKind.Provider,"test.activate")):Result<Unit>.Success(default));
                if(AutoUnload&&scene.Unload?.State==BackendState.Running)scene.Unload.Complete(FailUnload?Result<Unit>.Failure(new FrameworkError(ErrorKind.CleanupFailed,"test.unload")):Result<Unit>.Success(default));
            }
        }
        internal async UniTask<T> Pump<T>(UniTask<T> task){for(var i=0;i<1200&&task.Status==UniTaskStatus.Pending;i++){Step();await UniTask.Yield();}Assert.AreNotEqual(UniTaskStatus.Pending,task.Status,"Scene operation did not drain");return await task;}
        internal async UniTask Frames(int count=5){for(var i=0;i<count;i++){Step();await UniTask.Yield();}}
        internal async UniTask Start(){Assert.IsTrue((await Pump(Context.StartAsync(Wait))).IsSuccess);}
        internal async UniTask<ISceneLease> Loaded(SceneLoadOptions? options=null,Scope owner=null,ResourceKey? key=null){var accepted=Load(owner,options,key);Assert.IsTrue(accepted.IsSuccess);var r=await Pump(accepted.Value.WaitAsync(Wait));Assert.IsTrue(r.IsSuccess,r.IsSuccess?"":r.Error.Code);return r.Value;}
        internal async UniTask Finish()
        {
            AutoLoad=AutoActivate=AutoUnload=true;FailLoad=FailUnload=FailActivate=false;
            await Pump(Context.CloseAsync(Wait));var result=await Pump(Context.RetryCleanupAsync(Cleanup,Wait));Assert.IsTrue(result.IsSuccess,result.IsSuccess?"":result.Error.Code);
            foreach(var owner in Owners){await Pump(owner.CloseAsync(Cleanup));Assert.IsTrue((await Pump(owner.RetryFailedAsync(Cleanup))).IsFullyDrained);}
            Assert.AreEqual(0,Scenes.RetainedSceneCount);
        }
    }
    readonly List<Env> environments=new List<Env>();Scene original;
    Env New(bool deferred=true,bool noScenes=false,UnitySceneProvider native=null,IPackageReadiness readiness=null){var e=new Env(deferred,noScenes,native,readiness);environments.Add(e);return e;}
    [SetUp] public void SetUp(){original=SceneManager.GetActiveScene();}
    [UnityTearDown] public IEnumerator TearDown()=>UniTask.ToCoroutine(async()=>{foreach(var e in environments)await e.Finish();environments.Clear();if(original.IsValid()&&original.isLoaded)SceneManager.SetActiveScene(original);});
    [UnityTest] public IEnumerator ColdRequestDoesNotStartBackend()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Load();Assert.AreEqual(0,e.Fake.SceneStartCount);});
    [UnityTest] public IEnumerator RequestWaitIsSingleUse()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var r=e.Load().Value;Assert.IsTrue((await e.Pump(r.WaitAsync(Wait))).IsSuccess);Assert.IsFalse((await r.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1,e.Fake.SceneStartCount);});
    [UnityTest] public IEnumerator InvalidKeyRejectedWithoutWork()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();Assert.IsFalse(e.Load(key:default(ResourceKey)).IsSuccess);Assert.AreEqual(0,e.Fake.SceneStartCount);});
    [UnityTest] public IEnumerator ClosedOwnerRejected()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var owner=e.Owner();await owner.CloseAsync(Cleanup);Assert.IsFalse(e.Load(owner).IsSuccess);});
    [UnityTest] public IEnumerator CanceledColdRequestStartsNothing()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();using(var c=new CancellationTokenSource()){c.Cancel();var r=await e.Load().Value.WaitAsync(OperationContext.Unlimited(c.Token));Assert.AreEqual(ErrorKind.Canceled,r.Error.Kind);Assert.AreEqual(0,e.Fake.SceneStartCount);}});
    [UnityTest] public IEnumerator ZeroTimeoutStartsNothing()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();Assert.AreEqual(ErrorKind.Timeout,(await e.Load().Value.WaitAsync(default)).Error.Kind);Assert.AreEqual(0,e.Fake.SceneStartCount);});
    [UnityTest] public IEnumerator CanceledLoadReclaimsLateScene()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.AutoLoad=false;using(var c=new CancellationTokenSource()){var op=e.Load().Value;var wait=op.WaitAsync(OperationContext.Unlimited(c.Token));await e.Frames();c.Cancel();Assert.AreEqual(ErrorKind.Canceled,(await e.Pump(wait)).Error.Kind);Assert.AreEqual(BackendState.Running,op.BackendState);Assert.AreEqual(1,e.Scenes.RetainedSceneCount);e.AutoLoad=true;await e.Frames(15);Assert.AreEqual(0,e.Scenes.RetainedSceneCount);}});
    [UnityTest] public IEnumerator OwnerCloseWaitsForLateLoad()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.AutoLoad=false;var owner=e.Owner();var load=e.Load(owner).Value.WaitAsync(Wait);await e.Frames();var close=owner.CloseAsync(Cleanup);await e.Frames();Assert.AreEqual(UniTaskStatus.Pending,close.Status);e.AutoLoad=true;Assert.IsTrue((await e.Pump(close)).IsFullyDrained);Assert.IsFalse((await e.Pump(load)).IsSuccess);});
    [UnityTest] public IEnumerator AdditiveSameAddressCreatesIndependentInstances()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var a=await e.Loaded();var b=await e.Loaded();Assert.AreNotSame(a,b);Assert.AreEqual(2,e.Fake.SceneStartCount);await e.Pump(a.UnloadAsync(Cleanup));Assert.AreEqual(SceneLeaseState.Active,b.State);});
    [UnityTest] public IEnumerator DeferredActivationIsExplicit()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var lease=await e.Loaded(Add(false));Assert.AreEqual(SceneLeaseState.AwaitingActivation,lease.State);Assert.IsTrue((await e.Pump(lease.BeginActivation().Value.WaitAsync(Wait))).IsSuccess);Assert.AreEqual(SceneLeaseState.Active,lease.State);});
    [UnityTest] public IEnumerator ActivationCancellationRetainsActualOperation()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var lease=await e.Loaded(Add(false));e.AutoActivate=false;using(var c=new CancellationTokenSource()){var operation=lease.BeginActivation().Value;var wait=operation.WaitAsync(OperationContext.Unlimited(c.Token));await e.Frames();c.Cancel();Assert.IsFalse((await e.Pump(wait)).IsSuccess);Assert.AreEqual(BackendState.Running,operation.BackendState);Assert.IsTrue(e.Fake.SceneAt(0).Scene.HoldsDependencies);e.AutoActivate=true;await e.Frames(15);Assert.AreEqual(SceneLeaseState.Released,lease.State);}});
    [UnityTest] public IEnumerator OwnerCloseDuringActivationWaitsActualEnd()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var owner=e.Owner();var lease=await e.Loaded(Add(false),owner);e.AutoActivate=false;var activation=lease.BeginActivation().Value.WaitAsync(Wait);await e.Frames();var close=owner.CloseAsync(Cleanup);await e.Frames();Assert.AreEqual(UniTaskStatus.Pending,close.Status);e.AutoActivate=true;Assert.IsTrue((await e.Pump(close)).IsFullyDrained);Assert.IsFalse((await e.Pump(activation)).IsSuccess);});
    [UnityTest] public IEnumerator UnloadRequiresConfirmation()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var lease=await e.Loaded();e.AutoUnload=false;var close=lease.UnloadAsync(Cleanup);await e.Frames();Assert.AreEqual(SceneLeaseState.Unloading,lease.State);Assert.IsTrue(e.Fake.SceneAt(0).Scene.HoldsDependencies);Assert.AreEqual(UniTaskStatus.Pending,close.Status);e.AutoUnload=true;Assert.IsTrue((await e.Pump(close)).IsSuccess);Assert.IsFalse(e.Fake.SceneAt(0).Scene.HoldsDependencies);});
    [UnityTest] public IEnumerator UnloadFailureRequiresExplicitRetry()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var lease=await e.Loaded();e.FailUnload=true;Assert.IsFalse((await e.Pump(lease.UnloadAsync(Cleanup))).IsSuccess);Assert.IsTrue(e.Scenes.NeedsRecovery);Assert.IsFalse(e.Load().IsSuccess);e.FailUnload=false;Assert.IsFalse((await lease.UnloadAsync(Cleanup)).IsSuccess);Assert.IsTrue((await e.Pump(lease.RetryUnloadAsync(Cleanup))).IsSuccess);Assert.IsFalse(e.Scenes.NeedsRecovery);});
    [UnityTest] public IEnumerator DuplicateSuccessfulUnloadRejected()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var lease=await e.Loaded();await e.Pump(lease.UnloadAsync(Cleanup));Assert.IsFalse((await lease.UnloadAsync(Cleanup)).IsSuccess);});
    [UnityTest] public IEnumerator ConcurrentUnloadJoinsOneAttempt()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var lease=await e.Loaded();e.AutoUnload=false;var a=lease.UnloadAsync(Cleanup);var native=e.Fake.SceneAt(0).Scene.Unload;var b=lease.UnloadAsync(Cleanup);Assert.AreSame(native,e.Fake.SceneAt(0).Scene.Unload);e.AutoUnload=true;Assert.IsTrue((await e.Pump(a)).IsSuccess);Assert.IsTrue((await e.Pump(b)).IsSuccess);});
    [UnityTest] public IEnumerator SingleTransitionRejectsOverlap()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var pending=await e.Loaded(Single(false));Assert.IsFalse((await e.Load().Value.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1,e.Fake.SceneStartCount);await e.Pump(pending.BeginActivation().Value.WaitAsync(Wait));});
    [UnityTest] public IEnumerator SingleActivatesBeforeUnloadingOld()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var old=await e.Loaded();var next=await e.Loaded(Single(false));Assert.AreEqual(SceneLeaseState.Active,old.State);e.AutoUnload=false;var commit=next.BeginActivation().Value.WaitAsync(Wait);await e.Frames();Assert.AreEqual(SceneLeaseState.Active,next.State);Assert.AreEqual(SceneLeaseState.Unloading,old.State);Assert.AreEqual(UniTaskStatus.Pending,commit.Status);e.AutoUnload=true;Assert.IsTrue((await e.Pump(commit)).IsSuccess);});
    [UnityTest] public IEnumerator SingleUnloadFailureBlocksNewTransitions()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();await e.Loaded();e.FailUnload=true;var result=await e.Pump(e.Load(options:Single()).Value.WaitAsync(Wait));Assert.IsFalse(result.IsSuccess);await e.Frames();Assert.IsTrue(e.Scenes.NeedsRecovery);Assert.IsFalse(e.Load().IsSuccess);e.FailUnload=false;Assert.IsTrue((await e.Pump(e.Scenes.RetryRecoveryAsync(Cleanup))).IsSuccess);});
    [UnityTest] public IEnumerator UnsupportedDeferredLoadRejected()=>UniTask.ToCoroutine(async()=>{var e=New(false);await e.Start();Assert.AreEqual(ErrorKind.NotSupported,e.Load(options:Add(false)).Error.Kind);Assert.AreEqual(0,e.Fake.SceneStartCount);});
    [UnityTest] public IEnumerator ProviderFailurePreservesClassification()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.FailLoad=true;Assert.AreEqual(ErrorKind.Provider,(await e.Pump(e.Load().Value.WaitAsync(Wait))).Error.Kind);});
    [UnityTest] public IEnumerator ContextCloseOwnsPendingLoad()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.AutoLoad=false;var wait=e.Load().Value.WaitAsync(Wait);await e.Frames();var close=e.Context.CloseAsync(Wait);await e.Frames();Assert.AreEqual(UniTaskStatus.Pending,close.Status);e.AutoLoad=true;Assert.IsTrue((await e.Pump(close)).IsSuccess);Assert.IsFalse((await e.Pump(wait)).IsSuccess);});
    [UnityTest] public IEnumerator VersionMismatchRejected()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();Assert.AreEqual(ErrorKind.InvalidArgument,e.Load(key:Key(version:"v2")).Error.Kind);});
    [UnityTest] public IEnumerator ActivationFailureReclaimsScene()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var lease=await e.Loaded(Add(false));e.FailActivate=true;Assert.AreEqual(ErrorKind.Provider,(await e.Pump(lease.BeginActivation().Value.WaitAsync(Wait))).Error.Kind);await e.Frames();Assert.AreEqual(SceneLeaseState.Released,lease.State);});
    [UnityTest] public IEnumerator ContextCloseCleansUnwaitedRequest()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Load();Assert.IsTrue((await e.Pump(e.Context.CloseAsync(Wait))).IsSuccess);Assert.AreEqual(0,e.Scenes.RetainedSceneCount);Assert.AreEqual(0,e.Fake.SceneStartCount);});
    [UnityTest] public IEnumerator FacadeCreatesSessionWithoutSceneModule()=>UniTask.ToCoroutine(async()=>{var e=New(noScenes:true);await e.Start();var game=new GameServices(e.Context);var result=await e.Pump(game.EnterSessionAsync("round",null,Wait));Assert.IsTrue(result.IsSuccess);Assert.IsNull(result.Value.Scene);Assert.AreEqual(ScopeKind.Session,result.Value.Scope.Kind);Assert.AreEqual(0,e.Fake.SceneStartCount);});
    sealed class Subscription:IDisposable {internal int Count;internal bool Fail;public void Dispose(){Count++;if(Fail)throw new InvalidOperationException("cleanup fault");}}
    [UnityTest] public IEnumerator FlowReplacesAndClosesOldSessionResources()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var first=(await e.Pump(e.Flow.EnterAsync("a",null,Wait))).Value;var subscription=new Subscription();first.Scope.TrackSubscription("subscription",subscription);var second=(await e.Pump(e.Flow.EnterAsync("b",null,Wait))).Value;Assert.AreEqual(1,subscription.Count);Assert.AreEqual(ScopeState.Closed,first.Scope.State);Assert.AreSame(second,e.Flow.Current);});
    [UnityTest] public IEnumerator FlowLoadFailureKeepsPreviousSession()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var old=(await e.Pump(e.Flow.EnterAsync("old",null,Wait))).Value;e.FailLoad=true;Assert.IsFalse((await e.Pump(e.Flow.EnterAsync("next",Key(),Wait))).IsSuccess);Assert.AreSame(old,e.Flow.Current);Assert.AreEqual(ScopeState.Open,old.Scope.State);});
    [UnityTest] public IEnumerator FlowCanceledBeforeCommitDoesNotPublish()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.AutoLoad=false;using(var c=new CancellationTokenSource()){var wait=e.Flow.EnterAsync("late",Key(),OperationContext.Unlimited(c.Token));await e.Frames();c.Cancel();Assert.IsFalse((await e.Pump(wait)).IsSuccess);e.AutoLoad=true;await e.Frames(20);Assert.IsNull(e.Flow.Current);Assert.AreEqual(0,e.Scenes.RetainedSceneCount);}});
    [UnityTest] public IEnumerator FlowOldCleanupFailurePreservesNewAndRequiresRetry()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var old=(await e.Pump(e.Flow.EnterAsync("old",null,Wait))).Value;var subscription=new Subscription{Fail=true};old.Scope.TrackSubscription("fault",subscription);Assert.IsFalse((await e.Pump(e.Flow.EnterAsync("new",null,Wait))).IsSuccess);Assert.AreEqual("new",e.Flow.Current.Id);Assert.AreEqual(SessionFlowState.Recovering,e.Flow.State);Assert.IsFalse((await e.Flow.EnterAsync("blocked",null,Wait)).IsSuccess);subscription.Fail=false;Assert.IsTrue((await e.Pump(e.Flow.RetryRecoveryAsync(Cleanup))).IsSuccess);Assert.AreEqual(SessionFlowState.Ready,e.Flow.State);});
    [UnityTest] public IEnumerator FlowRejectsOverlappingTransition()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.AutoLoad=false;var first=e.Flow.EnterAsync("a",Key(),Wait);Assert.IsFalse((await e.Flow.EnterAsync("b",null,Wait)).IsSuccess);e.AutoLoad=true;Assert.IsTrue((await e.Pump(first)).IsSuccess);});
    [UnityTest] public IEnumerator FlowContextCloseDrainsSessionScene()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var session=(await e.Pump(e.Flow.EnterAsync("a",Key(),Wait))).Value;var closing=e.Context.CloseAsync(Wait);Assert.AreNotEqual(ScopeState.Open,session.Scope.State);Assert.IsTrue((await e.Pump(closing)).IsSuccess);Assert.AreEqual(ScopeState.Closed,session.Scope.State);Assert.IsNull(e.Flow.Current);Assert.AreEqual(SceneLeaseState.Released,session.Scene.State);});

    [UnityTest] public IEnumerator SameBatchLoadCancellationCannotReplaceOldScene()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var old=await e.Loaded();e.AutoLoad=false;using(var c=new CancellationTokenSource()){var request=e.Load(options:Single()).Value;var waiting=request.WaitAsync(OperationContext.Unlimited(c.Token));c.Cancel();e.Fake.SceneAt(1).Succeed();Assert.AreEqual(SceneLeaseState.Active,old.State);Assert.IsNull(e.Fake.SceneAt(0).Scene.Unload);Assert.AreEqual(ErrorKind.Canceled,(await e.Pump(waiting)).Error.Kind);Assert.AreEqual(BackendState.Succeeded,request.BackendState);e.AutoLoad=true;await e.Frames();}});
    [UnityTest] public IEnumerator SameBatchActivationCancellationCannotCommitSingle()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var old=await e.Loaded();var next=await e.Loaded(Single(false));e.AutoActivate=false;using(var c=new CancellationTokenSource()){var activation=next.BeginActivation().Value;var waiting=activation.WaitAsync(OperationContext.Unlimited(c.Token));c.Cancel();e.Fake.SceneAt(1).Scene.Activation.Complete(Result<Unit>.Success(default));Assert.AreEqual(SceneLeaseState.Active,old.State);Assert.IsNull(e.Fake.SceneAt(0).Scene.Unload);Assert.AreEqual(ErrorKind.Canceled,(await e.Pump(waiting)).Error.Kind);Assert.AreEqual(BackendState.Succeeded,activation.BackendState);e.AutoActivate=true;await e.Frames();}});
    [UnityTest] public IEnumerator SameBatchFlowCancellationKeepsOldSession()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var old=(await e.Pump(e.Flow.EnterAsync("old",null,Wait))).Value;e.AutoLoad=false;using(var c=new CancellationTokenSource()){var waiting=e.Flow.EnterAsync("new",Key(),OperationContext.Unlimited(c.Token));c.Cancel();e.Fake.SceneAt(0).Succeed();Assert.IsFalse((await e.Pump(waiting)).IsSuccess);await e.Frames(15);Assert.AreSame(old,e.Flow.Current);Assert.AreEqual(ScopeState.Open,old.Scope.State);}});
    [UnityTest] public IEnumerator LoadTimeoutKeepsActualBackendOwned()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.AutoLoad=false;var request=e.Load().Value;var waiting=request.WaitAsync(new OperationContext(default,TimeSpan.FromMilliseconds(1)));await UniTask.Delay(10,ignoreTimeScale:true);Assert.AreEqual(ErrorKind.Timeout,(await e.Pump(waiting)).Error.Kind);Assert.AreEqual(BackendState.Running,request.BackendState);Assert.AreEqual(1,e.Scenes.RetainedSceneCount);e.AutoLoad=true;await e.Frames(15);Assert.AreEqual(0,e.Scenes.RetainedSceneCount);});
    sealed class ReadyGate:IPackageReadiness
    {
        internal bool Open=true;public string Package=>"main";public string ContentVersion=>"v1";public PackageState State=>Open?PackageState.Ready:PackageState.Failed;
        public Result CheckReady(string package,string version)=>Open&&package==Package&&version==ContentVersion?Result.Success():Result.Failure(new FrameworkError(ErrorKind.NotReady,"test.package.closed"));
    }
    [UnityTest] public IEnumerator PackageStoppedDuringLoadRejectsLateCommit()=>UniTask.ToCoroutine(async()=>{var gate=new ReadyGate();var e=New(readiness:gate);await e.Start();e.AutoLoad=false;var operation=e.Load(options:Single()).Value;var waiting=operation.WaitAsync(Wait);gate.Open=false;e.Fake.SceneAt(0).Succeed();Assert.AreEqual(ErrorKind.NotReady,(await e.Pump(waiting)).Error.Kind);Assert.AreEqual(BackendState.Succeeded,operation.BackendState);await e.Frames(15);Assert.AreEqual(0,e.Scenes.RetainedSceneCount);});
    static UnitySceneProvider Native(Func<Scene,AsyncOperation> unload=null)
    {
#if UNITY_EDITOR
        return new UnitySceneProvider("native","main","v1",new Dictionary<string,string>{{"a","Assets/__S304Fixture/A.unity"},{"b","Assets/__S304Fixture/B.unity"}},path=>UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path,new LoadSceneParameters(LoadSceneMode.Additive)),unload);
#else
        throw new NotSupportedException("Editor fixtures only");
#endif
    }
    [UnityTest] public IEnumerator NativeAdditiveActivationAndUnload()=>UniTask.ToCoroutine(async()=>{var provider=Native();var e=New(native:provider);await e.Start();var lease=await e.Loaded(key:Key(provider:"native"));Assert.AreEqual("Assets/__S304Fixture/A.unity",SceneManager.GetActiveScene().path);Assert.AreEqual(1,provider.RetainedHandleCount);Assert.IsTrue((await e.Pump(lease.UnloadAsync(Cleanup))).IsSuccess);Assert.AreEqual(0,provider.RetainedHandleCount);});
    [UnityTest] public IEnumerator NativeDeferredOwnerCloseCompletesAndUnloads()=>UniTask.ToCoroutine(async()=>{var provider=Native();var e=New(native:provider);await e.Start();var owner=e.Owner();var lease=await e.Loaded(Add(false),owner,Key(provider:"native"));Assert.AreEqual(SceneLeaseState.AwaitingActivation,lease.State);Assert.IsTrue((await e.Pump(owner.CloseAsync(Cleanup))).IsFullyDrained);Assert.AreEqual(SceneLeaseState.Released,lease.State);Assert.AreEqual(0,provider.RetainedHandleCount);});
    [UnityTest] public IEnumerator NativeSinglePreservesExternalBootstrap()=>UniTask.ToCoroutine(async()=>{var provider=Native();var e=New(native:provider);await e.Start();var old=await e.Loaded(key:Key(provider:"native"));var next=await e.Loaded(Single(),key:Key("b","native"));Assert.AreEqual(SceneLeaseState.Released,old.State);Assert.AreEqual(SceneLeaseState.Active,next.State);Assert.IsTrue(original.IsValid()&&original.isLoaded);Assert.AreEqual("Assets/__S304Fixture/B.unity",SceneManager.GetActiveScene().path);});
    [UnityTest] public IEnumerator NativeUnloadFaultRetainsHandleUntilRetry()=>UniTask.ToCoroutine(async()=>{var reject=true;var provider=Native(scene=>reject?null:SceneManager.UnloadSceneAsync(scene));var e=New(native:provider);await e.Start();var lease=await e.Loaded(key:Key(provider:"native"));Assert.IsFalse((await e.Pump(lease.UnloadAsync(Cleanup))).IsSuccess);Assert.AreEqual(1,provider.RetainedHandleCount);Assert.IsTrue(SceneManager.GetSceneByPath("Assets/__S304Fixture/A.unity").isLoaded);reject=false;Assert.IsTrue((await e.Pump(lease.RetryUnloadAsync(Cleanup))).IsSuccess);Assert.AreEqual(0,provider.RetainedHandleCount);});
    [UnityTest] public IEnumerator NativeRepeatedAddressUsesDistinctSceneHandles()=>UniTask.ToCoroutine(async()=>{var provider=Native();var e=New(native:provider);await e.Start();var a=await e.Loaded(key:Key(provider:"native"));var first=SceneManager.GetActiveScene();var b=await e.Loaded(key:Key(provider:"native"));var second=SceneManager.GetActiveScene();Assert.AreNotEqual(first.handle,second.handle);await e.Pump(a.UnloadAsync(Cleanup));Assert.IsTrue(second.IsValid()&&second.isLoaded);Assert.AreEqual(SceneLeaseState.Active,b.State);});
}
