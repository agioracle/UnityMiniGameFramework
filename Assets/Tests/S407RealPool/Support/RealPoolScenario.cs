using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Host;
using MiniFramework.Platform;
using MiniFramework.Pool;
using MiniFramework.Providers.WeChat;
using MiniFramework.Runtime;
using UnityEngine;
using UnityEngine.Scripting;
using Object=UnityEngine.Object;
namespace MiniFramework.S407RealPool
{
    [Serializable, Preserve]
    public sealed class RealPoolCase
    {
        [Preserve] public string name, error="";
        [Preserve] public bool passed, activeProtected, prefetchRejected, criticalCompleted, prefetchRestored, closeWaited, lateFenced;
        [Preserve] public bool injectedReleaseAcknowledgementHold;
        [Preserve] public int created, destroyed, destroyFrames, maxDestroyedPerFrame, trimBudget=2, passes, coalesced, releases, releaseOrderViolations;
        [Preserve] public int modules, buckets, active, idle, instances, pending, assetEntries, providerHandles, pendingReleases, schedulerPending, schedulerQueued, sourceListeners, platformListeners, queuedCallbacks;
    }
    // Shared by PlayMode and the independent SDK probe. No hand-pumped scheduler or fake pool.
    public sealed class RealPoolScenario
    {
        public static readonly string[] Cases={"burst_active_prefetch", "close_during_destroy", "close_during_release"};
        static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);
        readonly ObservedMemoryApi api;
        readonly GameObject hostObject;
        readonly RuntimeHostModule host;
        readonly Scope owner=new Scope("s407-real-pool-owner");
        readonly ResourcesFixtureProvider provider=new ResourcesFixtureProvider();
        readonly ResourceWorkScheduler scheduler;
        readonly AssetsService assets;
        readonly GameObjectPool pool;
        readonly PlatformService platform;
        readonly PlatformMemoryPressureSource source;
        readonly MemoryPressureController pressure;
        readonly GameContext context;
        readonly List<GameObject> instances=new List<GameObject>();
        readonly List<string> addresses=new List<string>();
        readonly Dictionary<int,int> destroyedPerFrame=new Dictionary<int,int>();
        int destroyed;
        public RealPoolScenario(ObservedMemoryApi api)
        {
            this.api=api;
            hostObject=new GameObject("S407RealPoolHost"); Object.DontDestroyOnLoad(hostObject);
            host=new RuntimeHostModule(hostObject.AddComponent<RuntimeHost>());
            scheduler=new ResourceWorkScheduler(new ResourceScheduleBudget(2,2,2,TimeSpan.FromMilliseconds(10)),hostModuleId:host.Id);
            assets=new AssetsService(provider,"main","v1",Cleanup,scheduler:scheduler,schedulerModuleId:scheduler.Id);
            pool=new GameObjectPool(assets,scheduler,new PoolCapacity(8,8),Cleanup,hostModuleId:host.Id);
            platform=new PlatformService(new WeChatBackend(api));
            source=new PlatformMemoryPressureSource(platform);
            pressure=new MemoryPressureController(source,scheduler,pool,2,Cleanup,sourceModuleId:source.Id);
            provider.HasLiveInstances=address=>{for(int i=0;i<instances.Count;i++)if(addresses[i]==address&&instances[i])return true;return false;};
            context=new GameContext(new[]{Reg(host,typeof(IRuntimeScheduler),typeof(IApplicationLifecycle)),
                Reg(scheduler,typeof(IResourceWorkScheduler)),Reg(assets,typeof(IAssetsService)),Reg(pool,typeof(IPoolService<GameObject>)),
                Reg(platform,typeof(IPlatformCapabilities),typeof(IPlatformLifecycle)),Reg(source,typeof(IMemoryPressureSource)),Reg(pressure,typeof(IMemoryPressureHandler))},Cleanup);
        }
        static ModuleRegistration Reg(IModule module,params Type[] services)=>new ModuleRegistration(module.Id,module.Dependencies,()=>module,services);
        static void Require(bool value,string code){if(!value)throw new InvalidOperationException(code);}
        static void Check(Result value,string code){if(!value.IsSuccess)throw new InvalidOperationException(code+":"+value.Error.Code);}
        static async UniTask Until(Func<bool> done,string code)
        {
            var deadline=Time.realtimeSinceStartupAsDouble+45;
            while(!done()){Require(Time.realtimeSinceStartupAsDouble<deadline,code+".timeout");await UniTask.Yield();}
        }
        static async UniTask<Result> Bounded(UniTask<Result> task,string code)
        {
            var actual=task.Preserve(); await Until(()=>actual.Status!=UniTaskStatus.Pending,code);return await actual;
        }
        async UniTask<IPoolRent<GameObject>> Rent(string address)
        {
            var accepted=pool.Spawn(provider.Key(address),owner);Require(accepted.IsSuccess,"spawn.accept");
            var actual=accepted.Value.WaitAsync(new OperationContext(default,TimeSpan.FromSeconds(30))).Preserve();
            await Until(()=>actual.Status!=UniTaskStatus.Pending,"spawn");var result=await actual;
            Require(result.IsSuccess,"spawn.complete");var rent=result.Value;var go=rent.Value;
            instances.Add(go);addresses.Add(address);
            go.AddComponent<DestroyWitness>().Destroyed=frame=>{destroyed++;destroyedPerFrame.TryGetValue(frame,out var count);destroyedPerFrame[frame]=count+1;};
            go.SetActive(true); // Pool intentionally hands off inactive instances; the consumer activates them.
            return rent;
        }
        async UniTask<List<IPoolRent<GameObject>>> Idle(int count,string address)
        {
            var rents=new List<IPoolRent<GameObject>>();for(int i=0;i<count;i++)rents.Add(await Rent(address));
            foreach(var rent in rents)Check(await rent.ReturnAsync(Cleanup),"rent.return");return rents;
        }
        async UniTask Burst(RealPoolCase report)
        {
            var active=await Rent("mixed");var activeObject=active.Value;
            await Idle(5,"mixed");await Idle(2,"cold");
            Require(provider.Handles==2&&pool.ActiveCount==1&&pool.IdleCount==7,"seed.ownership");
            api.Emit();api.Emit();api.Emit();await Until(()=>pressure.IsRunning,"pressure.start");
            report.prefetchRejected=!scheduler.ScheduleFrame(()=>Result<Unit>.Success(default),ResourcePriority.Prefetch).IsSuccess;
            Require(scheduler.IsPrefetchSuppressed&&report.prefetchRejected,"prefetch.gated");
            var critical=scheduler.ScheduleFrame(()=>Result<Unit>.Success(default),ResourcePriority.Critical);
            Require(critical.IsSuccess,"critical.accept");var work=critical.Value.Completion.Preserve();
            await Until(()=>work.Status!=UniTaskStatus.Pending,"critical");report.criticalCompleted=(await work).IsSuccess;
            await Until(()=>!pressure.IsRunning,"pressure.complete");Check(pressure.LastResult,"trim");
            report.activeProtected=activeObject&&active.State==PoolRentState.Active&&pool.ActiveCount==1&&pool.IdleCount==0&&pool.BucketCount==1&&provider.Handles==1&&assets.RetainedEntryCount==1;
            Require(report.activeProtected&&destroyed==7&&provider.ReleaseStarts==1,"active.protected");
            Require(pressure.PassCount==1&&pressure.CoalescedCount==2,"burst.coalesced");
            var prefetch=scheduler.ScheduleFrame(()=>Result<Unit>.Success(default),ResourcePriority.Prefetch);
            Require(!scheduler.IsPrefetchSuppressed&&prefetch.IsSuccess,"prefetch.restore.accept");var restored=prefetch.Value.Completion.Preserve();
            await Until(()=>restored.Status!=UniTaskStatus.Pending,"prefetch.restore");report.prefetchRestored=(await restored).IsSuccess;
            Check(await active.ReturnAsync(Cleanup),"active.return");api.Emit();
            await Until(()=>pressure.PassCount==2&&!pressure.IsRunning,"second.trim");Check(pressure.LastResult,"second.trim.result");
            Require(destroyed==8&&pool.BucketCount==0&&provider.Handles==0&&assets.RetainedEntryCount==0,"last.active.reclaimed");
            Require(report.criticalCompleted&&report.prefetchRestored,"work.completed");
        }
        async UniTask Closing(RealPoolCase report,bool holdRelease)
        {
            report.injectedReleaseAcknowledgementHold=holdRelease;provider.HoldRelease=holdRelease;
            await Idle(holdRelease?2:8,"cold");api.Emit();
            await Until(()=>holdRelease?provider.PendingReleases==1:pressure.IsRunning,"close.barrier");
            if(holdRelease)Require(destroyed==2&&provider.Handles==1&&assets.RetainedEntryCount==1,"release.retained");
            var close=context.CloseAsync(OperationContext.Unlimited()).Preserve();
            report.closeWaited=close.Status==UniTaskStatus.Pending&&context.RetainedModuleCount>0&&scheduler.IsPrefetchSuppressed;
            Require(report.closeWaited,"close.did_not_wait");
            if(holdRelease)
            {
                await UniTask.NextFrame();await UniTask.NextFrame();
                Require(close.Status==UniTaskStatus.Pending&&provider.Handles==1&&scheduler.IsPrefetchSuppressed,"close.release_barrier");
                provider.ReleaseHeld();
            }
            Check(await Bounded(close,"close"),"close.complete");
        }
        public async UniTask<RealPoolCase> Run(string name)
        {
            var report=new RealPoolCase{name=name};
            try
            {
                Check(await Bounded(context.StartAsync(OperationContext.Unlimited()),"start"),"start.result");
                Require(platform.Capabilities.Get(PlatformFeature.MemoryWarning).IsUsable,"memory.capability");
                if(name==Cases[0])await Burst(report);
                else if(name==Cases[1])await Closing(report,false);
                else if(name==Cases[2])await Closing(report,true);
                else throw new InvalidOperationException("case.unknown");
            }
            catch(Exception ex){report.error=ex.Message;}
            finally
            {
                provider.ReleaseHeld();
                try
                {
                    Check(await Bounded(context.CloseAsync(OperationContext.Unlimited()),"final.close"),"final.close.result");
                    Require((await owner.CloseAsync(Cleanup)).IsFullyDrained,"owner.drain");
                    var passes=pressure.PassCount;api.EmitLate();for(int i=0;i<3;i++)await UniTask.NextFrame();
                    report.lateFenced=pressure.PassCount==passes&&platform.QueuedCount==0&&source.SubscriberCount==0;
                    Object.Destroy(hostObject);await Until(()=>!hostObject,"host.destroy");
                }
                catch(Exception ex){report.error+=(report.error.Length==0?"":";")+ex.Message;}
                report.created=instances.Count;report.destroyed=destroyed;report.destroyFrames=destroyedPerFrame.Count;
                foreach(var count in destroyedPerFrame.Values)report.maxDestroyedPerFrame=Math.Max(report.maxDestroyedPerFrame,count);
                report.passes=pressure.PassCount;report.coalesced=pressure.CoalescedCount;report.releases=provider.ReleaseStarts;report.releaseOrderViolations=provider.ReleaseOrderViolations;
                report.modules=context.RetainedModuleCount;report.buckets=pool.BucketCount;report.active=pool.ActiveCount;report.idle=pool.IdleCount;report.instances=pool.RetainedInstanceCount;report.pending=pool.PendingCount;
                report.assetEntries=assets.RetainedEntryCount;report.providerHandles=provider.Handles;report.pendingReleases=provider.PendingReleases;
                report.schedulerPending=scheduler.ReadDiagnostics().PendingOperations;report.schedulerQueued=scheduler.ReadDiagnostics().QueuedCallbacks;
                report.sourceListeners=source.SubscriberCount;report.platformListeners=platform.ReadDiagnostics().Listeners;report.queuedCallbacks=platform.QueuedCount;
                report.passed=report.error==""&&report.lateFenced&&report.created==report.destroyed&&report.maxDestroyedPerFrame<=report.trimBudget&&report.releaseOrderViolations==0&&
                    report.modules==0&&report.buckets==0&&report.active==0&&report.idle==0&&report.instances==0&&report.pending==0&&report.assetEntries==0&&report.providerHandles==0&&report.pendingReleases==0&&report.schedulerPending==0&&report.schedulerQueued==0&&report.sourceListeners==0&&report.platformListeners==0&&report.queuedCallbacks==0;
            }
            return report;
        }
    }
}
