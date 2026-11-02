using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Host;
using MiniFramework.Pool;
using MiniFramework.Providers.Fake;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;
namespace MiniFramework.Pool.Tests
{
    public sealed class GameObjectPoolTests
    {
        private static CleanupContext Cleanup=>new CleanupContext(CancellationToken.None,Timeout.InfiniteTimeSpan);
        private static ResourceKey Key(string name="prefab")=>new ResourceKey("fake","main","v1",name,typeof(GameObject));
        private readonly List<Env> environments=new List<Env>();
        private async UniTask<Env> New(int max=8,int idle=4,int units=2,int buckets=8,bool host=false)
        {var e=new Env(max,idle,units,buckets,host);environments.Add(e);await e.Init();return e;}
        private sealed class Env
        {
            internal readonly FakeResourceProvider Provider=new FakeResourceProvider();internal readonly ResourceWorkScheduler Scheduler;
            internal readonly GameObjectPool Pool;internal readonly AssetsService Assets;internal GameContext Context;
            internal readonly GameObject Prefab;internal readonly Scope Owner=new Scope("test-owner");
            internal bool Loads=true,Releases=true;internal GameObject HostObject;internal RuntimeHostModule Host;
            internal Env(int max,int idle,int units,int buckets,bool host)
            {
                Prefab=new GameObject("pool-test-template");Prefab.SetActive(false);Prefab.transform.localScale=new Vector3(2,3,4);
                if(host){HostObject=new GameObject("pool-test-host");Host=new RuntimeHostModule(HostObject.AddComponent<RuntimeHost>());}
                Scheduler=new ResourceWorkScheduler(new ResourceScheduleBudget(2,2,units,TimeSpan.FromSeconds(1)),hostModuleId:Host?.Id);
                Assets=new AssetsService(Provider,"main","v1",Cleanup,scheduler:Scheduler,schedulerModuleId:Scheduler.Id);
                Pool=new GameObjectPool(Assets,Scheduler,new PoolCapacity(max,idle,buckets),Cleanup,hostModuleId:Host?.Id);
            }
            internal async UniTask Init()
            {
                var list=new List<ModuleRegistration>();if(Host!=null)list.Add(new ModuleRegistration(Host.Id,Host.Dependencies,()=>Host,typeof(IRuntimeScheduler)));
                list.Add(new ModuleRegistration(Scheduler.Id,Scheduler.Dependencies,()=>Scheduler,typeof(IResourceWorkScheduler)));
                list.Add(new ModuleRegistration(Assets.Id,Assets.Dependencies,()=>Assets,typeof(IAssetsService)));
                list.Add(new ModuleRegistration(Pool.Id,Pool.Dependencies,()=>Pool,typeof(IPoolService<GameObject>)));
                Context=new GameContext(list,Cleanup);Assert.IsTrue((await Context.StartAsync(OperationContext.Unlimited())).IsSuccess);
            }
            internal void Step()
            {
                if(Host==null){Scheduler.Tick();Pool.PollDestroyed();}
                for(var i=0;i<Provider.AssetStartCount;i++)
                {
                    var load=Provider.AssetAt<GameObject>(i);if(Loads&&load.Operation.State==BackendState.Running)load.Succeed(Prefab);
                    if(Releases)load.Asset?.ReleaseAttempt?.Complete(Result<Unit>.Success(default));
                }
            }
            internal async UniTask<T> Pump<T>(UniTask<T> task)
            {for(var i=0;task.Status==UniTaskStatus.Pending&&i<600;i++){Step();await UniTask.Yield();}Assert.AreNotEqual(UniTaskStatus.Pending,task.Status,"Operation did not drain in 600 frames");return await task;}
            internal async UniTask<IPoolRent<GameObject>> Rent(string name="prefab",IScope owner=null)
            {var a=Pool.Spawn(Key(name),owner??Owner);Assert.IsTrue(a.IsSuccess);var r=await Pump(a.Value.WaitAsync(OperationContext.Unlimited()));Assert.IsTrue(r.IsSuccess,r.IsSuccess?"":r.Error.Code);return r.Value;}
            internal async UniTask Finish()
            {
                Loads=Releases=true;await Pump(Owner.CloseAsync(Cleanup));
                var result=await Pump(Context.CloseAsync(OperationContext.Unlimited()));
                if(!result.IsSuccess)result=await Pump(Context.CloseAsync(OperationContext.Unlimited()));
                Assert.IsTrue(result.IsSuccess);Assert.AreEqual(0,Pool.BucketCount);Assert.AreEqual(0,Pool.RetainedInstanceCount);Assert.AreEqual(0,Assets.RetainedEntryCount);
                Object.DestroyImmediate(Prefab);if(HostObject!=null)Object.DestroyImmediate(HostObject);
            }
        }
        private sealed class CleanupProbe:IAsyncCleanup
        {
            public string Id=>"dynamic";public int Calls;public bool Fail,Hold;
            public async UniTask<Result> CleanupAsync(CleanupContext context){Calls++;while(Hold)await UniTask.Yield();return Fail?Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"injected")):Result.Success();}
        }
        [UnityTearDown] public IEnumerator TearDown()=>UniTask.ToCoroutine(async()=>{foreach(var e in environments)await e.Finish();environments.Clear();});
        [Test] public void CapacityRejectsInvalidValues(){Assert.Throws<ArgumentOutOfRangeException>(()=>new PoolCapacity(0,0));Assert.Throws<ArgumentOutOfRangeException>(()=>new PoolCapacity(1,2));Assert.Throws<ArgumentOutOfRangeException>(()=>new PoolCapacity(1,0,0));}
        [UnityTest] public IEnumerator ColdRequestDoesNotLoadOrReserve()=>UniTask.ToCoroutine(async()=>{var e=await New();e.Pool.Spawn(Key(),e.Owner);e.Step();Assert.AreEqual(0,e.Provider.AssetStartCount);Assert.AreEqual(0,e.Pool.PendingCount);});
        [UnityTest] public IEnumerator InactiveCloneCanBeActivatedAndReusedWithNewScope()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=await e.Rent();var go=a.Value;Assert.IsFalse(go.activeSelf);go.SetActive(true);Assert.IsTrue(go.activeInHierarchy);var first=a.RentScope;Assert.IsTrue((await a.ReturnAsync(Cleanup)).IsSuccess);var b=await e.Rent();Assert.AreSame(go,b.Value);Assert.AreNotSame(first,b.RentScope);Assert.AreEqual(ScopeState.Closed,first.State);Assert.AreEqual(1,e.Pool.CreatedCount);});
        [UnityTest] public IEnumerator DuplicateReturnCannotReturnLaterRental()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=await e.Rent();await a.ReturnAsync(Cleanup);var b=await e.Rent();Assert.AreEqual(ErrorKind.InvalidState,(await a.ReturnAsync(Cleanup)).Error.Kind);Assert.AreEqual(PoolRentState.Active,b.State);Assert.Throws<InvalidOperationException>(()=>{var unused=a.Value;});});
        [UnityTest] public IEnumerator ConcurrentSpawnSharesPrefabAndCreatesDistinctInstances()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=e.Pool.Spawn(Key(),e.Owner).Value.WaitAsync(OperationContext.Unlimited());var b=e.Pool.Spawn(Key(),e.Owner).Value.WaitAsync(OperationContext.Unlimited());var ra=await e.Pump(a);var rb=await e.Pump(b);Assert.AreNotSame(ra.Value.Value,rb.Value.Value);Assert.AreEqual(1,e.Provider.AssetStartCount);Assert.AreEqual(2,e.Pool.ActiveCount);});
        [UnityTest] public IEnumerator InstanceCapacityIncludesPendingReservations()=>UniTask.ToCoroutine(async()=>{var e=await New(1,1);e.Loads=false;var a=e.Pool.Spawn(Key(),e.Owner).Value.WaitAsync(OperationContext.Unlimited());var b=await e.Pump(e.Pool.Spawn(Key(),e.Owner).Value.WaitAsync(OperationContext.Unlimited()));Assert.AreEqual(ErrorKind.Unavailable,b.Error.Kind);Assert.AreEqual(1,e.Pool.PendingCount);e.Loads=true;Assert.IsTrue((await e.Pump(a)).IsSuccess);});
        [UnityTest] public IEnumerator BucketCapacityRejectsUntilEvicted()=>UniTask.ToCoroutine(async()=>{var e=await New(buckets:1);var a=await e.Rent();await a.ReturnAsync(Cleanup);var failed=await e.Pump(e.Pool.Spawn(Key("b"),e.Owner).Value.WaitAsync(OperationContext.Unlimited()));Assert.AreEqual(ErrorKind.Unavailable,failed.Error.Kind);Assert.IsTrue((await e.Pump(e.Pool.EvictAsync(Key(),Cleanup))).IsSuccess);await e.Rent("b");});
        [UnityTest] public IEnumerator MaxIdleDestroysOverflowBeforeReleasingPrefab()=>UniTask.ToCoroutine(async()=>{var e=await New(2,1);var a=await e.Rent();var b=await e.Rent();var dead=b.Value;await a.ReturnAsync(Cleanup);await e.Pump(b.ReturnAsync(Cleanup));Assert.IsTrue(dead==null);Assert.AreEqual(1,e.Pool.IdleCount);Assert.AreEqual(AssetLeaseState.Active,e.Provider.AssetAt<GameObject>(0).Asset.State);});
        [UnityTest] public IEnumerator ZeroIdleDestroysEveryReturnedClone()=>UniTask.ToCoroutine(async()=>{var e=await New(2,0);var a=await e.Rent();var dead=a.Value;await e.Pump(a.ReturnAsync(Cleanup));Assert.IsTrue(dead==null);Assert.AreEqual(0,e.Pool.RetainedInstanceCount);});
        [UnityTest] public IEnumerator DynamicScopeClosesBeforeReuse()=>UniTask.ToCoroutine(async()=>{var e=await New(1,1);var a=await e.Rent();var probe=new CleanupProbe{Hold=true};a.RentScope.Cleanup.Register(probe);var returning=a.ReturnAsync(Cleanup);Assert.AreEqual(0,e.Pool.IdleCount);Assert.AreEqual(1,e.Pool.ActiveCount);var failed=await e.Pump(e.Pool.Spawn(Key(),e.Owner).Value.WaitAsync(OperationContext.Unlimited()));Assert.IsFalse(failed.IsSuccess);probe.Hold=false;Assert.IsTrue((await e.Pump(returning)).IsSuccess);Assert.AreEqual(1,probe.Calls);Assert.AreEqual(1,e.Pool.IdleCount);});
        [UnityTest] public IEnumerator DynamicFailureRequiresExplicitRetry()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=await e.Rent();var probe=new CleanupProbe{Fail=true};a.RentScope.Cleanup.Register(probe);Assert.IsFalse((await a.ReturnAsync(Cleanup)).IsSuccess);Assert.AreEqual(PoolRentState.ReturnFailed,a.State);Assert.AreEqual(0,e.Pool.IdleCount);Assert.IsFalse((await a.ReturnAsync(Cleanup)).IsSuccess);Assert.AreEqual(1,probe.Calls);probe.Fail=false;Assert.IsTrue((await a.RetryReturnAsync(Cleanup)).IsSuccess);Assert.AreEqual(2,probe.Calls);});
        [UnityTest] public IEnumerator ReturnWaitTimeoutDoesNotReleaseActiveSlot()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=await e.Rent();var probe=new CleanupProbe{Hold=true};a.RentScope.Cleanup.Register(probe);var result=await a.ReturnAsync(new CleanupContext(default,TimeSpan.Zero));Assert.AreEqual(ErrorKind.Timeout,result.Error.Kind);Assert.AreEqual(1,e.Pool.ActiveCount);probe.Hold=false;await e.Pump(a.ReturnAsync(Cleanup));Assert.AreEqual(0,e.Pool.ActiveCount);});
        [UnityTest] public IEnumerator OwnerCloseReturnsActiveRent()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=await e.Rent();Assert.IsTrue((await e.Pump(e.Owner.CloseAsync(Cleanup))).IsFullyDrained);Assert.AreEqual(PoolRentState.Returned,a.State);Assert.AreEqual(0,e.Pool.ActiveCount);});
        [UnityTest] public IEnumerator CancelBeforeWaitNeverStartsLoad()=>UniTask.ToCoroutine(async()=>{var e=await New();var cts=new CancellationTokenSource();cts.Cancel();var a=e.Pool.Spawn(Key(),e.Owner).Value;var result=await a.WaitAsync(OperationContext.Unlimited(cts.Token));Assert.AreEqual(ErrorKind.Canceled,result.Error.Kind);e.Step();Assert.AreEqual(0,e.Provider.AssetStartCount);cts.Dispose();});
        [UnityTest] public IEnumerator ZeroDeadlineNeverStartsLoad()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=e.Pool.Spawn(Key(),e.Owner).Value;Assert.AreEqual(ErrorKind.Timeout,(await a.WaitAsync(default)).Error.Kind);Assert.AreEqual(0,e.Pool.PendingCount);});
        [UnityTest] public IEnumerator CancelDuringPrefabLoadRetainsPendingUntilActualCompletion()=>UniTask.ToCoroutine(async()=>{var e=await New();e.Loads=false;var cts=new CancellationTokenSource();var a=e.Pool.Spawn(Key(),e.Owner).Value.WaitAsync(OperationContext.Unlimited(cts.Token));e.Step();cts.Cancel();Assert.AreEqual(ErrorKind.Canceled,(await e.Pump(a)).Error.Kind);Assert.AreEqual(1,e.Pool.PendingCount);Assert.IsFalse((await e.Pool.EvictAsync(Key(),Cleanup)).IsSuccess);e.Loads=true;for(var i=0;i<5;i++){e.Step();await UniTask.Yield();}Assert.AreEqual(0,e.Pool.CreatedCount);Assert.AreEqual(0,e.Pool.PendingCount);cts.Dispose();});
        [UnityTest] public IEnumerator OwnerClosesWhileSpawnAwaitsPrefab()=>UniTask.ToCoroutine(async()=>{var e=await New();e.Loads=false;var a=e.Pool.Spawn(Key(),e.Owner).Value.WaitAsync(OperationContext.Unlimited());e.Step();var close=e.Owner.CloseAsync(Cleanup);Assert.AreEqual(UniTaskStatus.Pending,close.Status);e.Loads=true;Assert.IsTrue((await e.Pump(close)).IsFullyDrained);Assert.IsFalse((await a).IsSuccess);Assert.AreEqual(0,e.Pool.CreatedCount);});
        [UnityTest] public IEnumerator QueuedFrameCancellationNeverInstantiates()=>UniTask.ToCoroutine(async()=>{var e=await New();var cts=new CancellationTokenSource();var a=e.Pool.Spawn(Key(),e.Owner).Value.WaitAsync(OperationContext.Unlimited(cts.Token));e.Scheduler.Tick();e.Provider.AssetAt<GameObject>(0).Succeed(e.Prefab);await UniTask.Yield();Assert.AreEqual(1,e.Scheduler.QueuedFrameUnits);cts.Cancel();Assert.IsFalse((await e.Pump(a)).IsSuccess);for(var i=0;i<3;i++){e.Step();await UniTask.Yield();}Assert.AreEqual(0,e.Pool.CreatedCount);cts.Dispose();});
        [UnityTest] public IEnumerator ExternalDestroyActiveClosesRentAndRecoversCapacity()=>UniTask.ToCoroutine(async()=>{var e=await New(1,1);var a=await e.Rent();var probe=new CleanupProbe();a.RentScope.Cleanup.Register(probe);Object.Destroy(a.Value);for(var i=0;i<4;i++){await UniTask.Yield();e.Step();}Assert.AreEqual(PoolRentState.Returned,a.State);Assert.AreEqual(1,probe.Calls);Assert.AreEqual(0,e.Pool.ActiveCount);await e.Rent();});
        [UnityTest] public IEnumerator ExternalDestroyNeverActivatedIdleIsDetected()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=await e.Rent();var go=a.Value;await a.ReturnAsync(Cleanup);Object.Destroy(go);for(var i=0;i<4;i++){await UniTask.Yield();e.Step();}Assert.AreEqual(0,e.Pool.IdleCount);Assert.AreEqual(0,e.Pool.RetainedInstanceCount);await e.Rent();Assert.AreEqual(2,e.Pool.CreatedCount);});
        [UnityTest] public IEnumerator EvictionRejectsActiveInstances()=>UniTask.ToCoroutine(async()=>{var e=await New();await e.Rent();Assert.AreEqual(ErrorKind.Unavailable,(await e.Pool.EvictAsync(Key(),Cleanup)).Error.Kind);Assert.AreEqual(AssetLeaseState.Active,e.Provider.AssetAt<GameObject>(0).Asset.State);});
        [UnityTest] public IEnumerator EvictionWaitsDestroyAndActualPrefabRelease()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=await e.Rent();var go=a.Value;await a.ReturnAsync(Cleanup);e.Releases=false;var evict=e.Pool.EvictAsync(Key(),Cleanup);Assert.AreEqual(UniTaskStatus.Pending,evict.Status);Assert.AreEqual(1,e.Pool.BucketCount);for(var i=0;i<4;i++){e.Step();await UniTask.Yield();}Assert.IsTrue(go==null);Assert.AreEqual(UniTaskStatus.Pending,evict.Status);e.Releases=true;Assert.IsTrue((await e.Pump(evict)).IsSuccess);Assert.AreEqual(0,e.Pool.BucketCount);});
        [UnityTest] public IEnumerator PrefabReleaseFailureRetainedForEvictionRetry()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=await e.Rent();await a.ReturnAsync(Cleanup);e.Releases=false;var evict=e.Pool.EvictAsync(Key(),Cleanup);var asset=e.Provider.AssetAt<GameObject>(0).Asset;for(var i=0;asset.ReleaseAttempt==null&&i<10;i++)await UniTask.Yield();Assert.IsNotNull(asset.ReleaseAttempt);asset.ReleaseAttempt.Complete(Result<Unit>.Failure(new FrameworkError(ErrorKind.CleanupFailed,"injected")));Assert.IsFalse((await e.Pump(evict)).IsSuccess);Assert.AreEqual(1,e.Pool.BucketCount);Assert.Greater(e.Pool.ReadDiagnostics().FailedCleanup,0);var rejected=await e.Pump(e.Pool.Spawn(Key(),e.Owner).Value.WaitAsync(OperationContext.Unlimited()));Assert.IsFalse(rejected.IsSuccess);e.Releases=true;Assert.IsTrue((await e.Pump(e.Pool.EvictAsync(Key(),Cleanup))).IsSuccess);});
        [UnityTest] public IEnumerator PrewarmCreatesDistinctIdleInstancesWithinFrameBudget()=>UniTask.ToCoroutine(async()=>{var e=await New(4,4,1);var warm=e.Pool.PrewarmAsync(Key(),4,e.Owner,OperationContext.Unlimited());var before=0;while(warm.Status==UniTaskStatus.Pending){e.Step();Assert.LessOrEqual(e.Pool.CreatedCount-before,1);before=e.Pool.CreatedCount;await UniTask.Yield();}Assert.IsTrue((await warm).IsSuccess);Assert.AreEqual(4,e.Pool.IdleCount);Assert.AreEqual(4,e.Pool.CreatedCount);Assert.AreEqual(0,e.Pool.ActiveCount);});
        [UnityTest] public IEnumerator RepeatedPrewarmReusesExistingIdleSet()=>UniTask.ToCoroutine(async()=>{var e=await New(4,4);Assert.IsTrue((await e.Pump(e.Pool.PrewarmAsync(Key(),3,e.Owner,OperationContext.Unlimited()))).IsSuccess);Assert.IsTrue((await e.Pump(e.Pool.PrewarmAsync(Key(),3,e.Owner,OperationContext.Unlimited()))).IsSuccess);Assert.AreEqual(3,e.Pool.CreatedCount);});
        [UnityTest] public IEnumerator PrewarmRejectsBeyondIdleCapacity()=>UniTask.ToCoroutine(async()=>{var e=await New(4,2);Assert.AreEqual(ErrorKind.InvalidArgument,(await e.Pool.PrewarmAsync(Key(),3,e.Owner,OperationContext.Unlimited())).Error.Kind);Assert.AreEqual(0,e.Pool.CreatedCount);});
        [UnityTest] public IEnumerator PrefabFailureNeverCreatesInstanceAndCanEvict()=>UniTask.ToCoroutine(async()=>{var e=await New();e.Loads=false;var a=e.Pool.Spawn(Key(),e.Owner).Value.WaitAsync(OperationContext.Unlimited());e.Step();e.Provider.AssetAt<GameObject>(0).Fail(new FrameworkError(ErrorKind.Provider,"missing"));Assert.IsFalse((await e.Pump(a)).IsSuccess);Assert.AreEqual(0,e.Pool.RetainedInstanceCount);Assert.IsTrue((await e.Pump(e.Pool.EvictAsync(Key(),Cleanup))).IsSuccess);e.Loads=true;await e.Rent();});
        [UnityTest] public IEnumerator WaitIsSingleUse()=>UniTask.ToCoroutine(async()=>{var e=await New();var request=e.Pool.Spawn(Key(),e.Owner).Value;await e.Pump(request.WaitAsync(OperationContext.Unlimited()));Assert.AreEqual(ErrorKind.InvalidState,(await request.WaitAsync(OperationContext.Unlimited())).Error.Kind);});
        [UnityTest] public IEnumerator PrototypeTransformRestoredAcrossReuse()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=await e.Rent();a.Value.transform.localScale=Vector3.one;await a.ReturnAsync(Cleanup);var b=await e.Rent();Assert.AreEqual(e.Prefab.transform.localScale,b.Value.transform.localScale);});
        [UnityTest] public IEnumerator ThousandSpawnReturnCyclesKeepOneCloneAndNoActiveRent()=>UniTask.ToCoroutine(async()=>{var e=await New(1,1);for(var i=0;i<1000;i++){var rent=await e.Rent();Assert.IsTrue((await rent.ReturnAsync(Cleanup)).IsSuccess);}Assert.AreEqual(1,e.Provider.AssetStartCount);Assert.AreEqual(1,e.Pool.CreatedCount);Assert.AreEqual(0,e.Pool.ActiveCount);Assert.AreEqual(0,e.Pool.PendingCount);Assert.AreEqual(1,e.Pool.IdleCount);Assert.IsTrue((await e.Pump(e.Pool.EvictAsync(Key(),Cleanup))).IsSuccess);Assert.AreEqual(0,e.Assets.RetainedEntryCount);});
        [UnityTest] public IEnumerator ContextCloseDrainsInstancesBeforeDependencies()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=await e.Rent();var go=a.Value;Assert.IsTrue((await e.Pump(e.Context.CloseAsync(OperationContext.Unlimited()))).IsSuccess);Assert.IsTrue(go==null);Assert.AreEqual(0,e.Pool.BucketCount);Assert.IsFalse(e.Pool.Spawn(Key(),e.Owner).IsSuccess);});
        [UnityTest] public IEnumerator RuntimeHostAutomaticallyFindsExternalDestroy()=>UniTask.ToCoroutine(async()=>{var e=await New(host:true);var a=await e.Rent();Object.Destroy(a.Value);for(var i=0;i<6;i++)await UniTask.Yield();Assert.AreEqual(0,e.Pool.ActiveCount);Assert.IsTrue((await e.Pump(e.Context.CloseAsync(OperationContext.Unlimited()))).IsSuccess);Assert.IsFalse(e.HostObject.GetComponent<RuntimeHost>().IsRunning);});
        [UnityTest] public IEnumerator DynamicAssetLeaseMustReleaseBeforeReuse()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=await e.Rent();var loaded=await e.Pump(e.Assets.Load<GameObject>(Key("dynamic"),a.RentScope).Value.WaitAsync(OperationContext.Unlimited()));Assert.IsTrue(loaded.IsSuccess);e.Releases=false;var returning=a.ReturnAsync(Cleanup);for(var i=0;i<3;i++)await UniTask.Yield();Assert.AreEqual(1,e.Pool.ActiveCount);Assert.AreEqual(0,e.Pool.IdleCount);Assert.AreEqual(AssetLeaseState.Active,e.Provider.AssetAt<GameObject>(0).Asset.State);Assert.AreEqual(AssetLeaseState.Releasing,e.Provider.AssetAt<GameObject>(1).Asset.State);e.Releases=true;Assert.IsTrue((await e.Pump(returning)).IsSuccess);Assert.AreEqual(AssetLeaseState.Released,e.Provider.AssetAt<GameObject>(1).Asset.State);Assert.AreEqual(AssetLeaseState.Active,e.Provider.AssetAt<GameObject>(0).Asset.State);});
        [UnityTest] public IEnumerator AllCanceledLatePrefabAutomaticallyEvictsEmptyBucket()=>UniTask.ToCoroutine(async()=>{var e=await New();e.Loads=false;var cts=new CancellationTokenSource();var a=e.Pool.Spawn(Key(),e.Owner).Value.WaitAsync(OperationContext.Unlimited(cts.Token));e.Step();cts.Cancel();Assert.IsFalse((await e.Pump(a)).IsSuccess);e.Loads=true;for(var i=0;i<12;i++){e.Step();await UniTask.Yield();}Assert.AreEqual(0,e.Pool.BucketCount);Assert.AreEqual(0,e.Assets.RetainedEntryCount);Assert.AreEqual(0,e.Pool.CreatedCount);cts.Dispose();});
        [UnityTest] public IEnumerator LastDestroyedReturnAutomaticallyReleasesEmptyBucket()=>UniTask.ToCoroutine(async()=>{var e=await New(1,0);var a=await e.Rent();Assert.IsTrue((await e.Pump(a.ReturnAsync(Cleanup))).IsSuccess);Assert.AreEqual(0,e.Pool.BucketCount);Assert.AreEqual(AssetLeaseState.Released,e.Provider.AssetAt<GameObject>(0).Asset.State);});
        [UnityTest] public IEnumerator ExternalDestroyFailureRemainsRetryable()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=await e.Rent();var probe=new CleanupProbe{Fail=true};a.RentScope.Cleanup.Register(probe);Object.Destroy(a.Value);for(var i=0;i<4;i++){await UniTask.Yield();e.Step();}Assert.AreEqual(PoolRentState.ReturnFailed,a.State);Assert.AreEqual(1,e.Pool.ReadDiagnostics().FailedCleanup);probe.Fail=false;Assert.IsTrue((await e.Pump(a.RetryReturnAsync(Cleanup))).IsSuccess);Assert.AreEqual(0,e.Pool.ActiveCount);});
        [UnityTest] public IEnumerator CancelPartialPrewarmReturnsAlreadyAcquiredRents()=>UniTask.ToCoroutine(async()=>{var e=await New(4,4,1);var cts=new CancellationTokenSource();var warm=e.Pool.PrewarmAsync(Key(),4,e.Owner,OperationContext.Unlimited(cts.Token));for(var i=0;e.Pool.CreatedCount<1&&i<20;i++){e.Step();await UniTask.Yield();}Assert.Greater(e.Pool.CreatedCount,0);cts.Cancel();Assert.AreEqual(ErrorKind.Canceled,(await e.Pump(warm)).Error.Kind);for(var i=0;i<5;i++){e.Step();await UniTask.Yield();}Assert.AreEqual(0,e.Pool.ActiveCount);Assert.AreEqual(0,e.Pool.PendingCount);cts.Dispose();});
        [UnityTest] public IEnumerator DisableCallbackReentrantReturnJoinsSameCleanup()=>UniTask.ToCoroutine(async()=>{var e=await New();var a=await e.Rent();var go=a.Value;var probe=go.AddComponent<ReturnOnDisableProbe>();UniTask<Result> nested=default;probe.Callback=()=>nested=a.ReturnAsync(Cleanup);go.SetActive(true);Assert.IsTrue((await e.Pump(a.ReturnAsync(Cleanup))).IsSuccess);Assert.IsTrue((await nested).IsSuccess);Assert.AreEqual(1,e.Pool.IdleCount);Assert.AreEqual(0,e.Pool.ActiveCount);});
    }
}

namespace MiniFramework.Pool.Tests
{
    public sealed class ReturnOnDisableProbe:MonoBehaviour
    {
        public Action Callback;
        private void OnDisable(){Callback?.Invoke();}
    }
}
