using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Platform;
using MiniFramework.Ads;
using MiniFramework.Providers.PlatformMock;
using MiniFramework.Providers.WeChat.Ads;
using MiniFramework.Tests.WeChatAds;
using NUnit.Framework;
using UnityEngine.TestTools;
public sealed class WeChatRewardedServiceTests
{
    static OperationContext Wait=>OperationContext.Unlimited();
    static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);
    static ClaimOwnerSnapshot Owner=>new ClaimOwnerSnapshot(ClaimOwnerMode.LocalOnly,"install-wechat-test",7,"local.ads");
    sealed class PlatformFixture : IPlatformBackend
    {
        public readonly MockPlatformBackend Inner=new MockPlatformBackend();
        public PlatformDescriptor Descriptor {get;}=new PlatformDescriptor("wechat",PlatformExecutionMode.Mock,"controlled-v1");
        public IPlatformSession Open(string generation,IPlatformCallbackSink sink)=>Inner.Open(generation,sink);
    }
    sealed class Env
    {
        public readonly RewardedApiFixture Api=new RewardedApiFixture();
        public readonly AdReceiptJournal Journal=new AdReceiptJournal();
        public readonly WeChatRewardedBackend Backend;
        public readonly PlatformFixture PlatformBackend=new PlatformFixture();
        public readonly AdsService Ads;public readonly GameContext Context;
        public Env(bool capability=true)
        {
            Backend=new WeChatRewardedBackend(Api,RewardedApiFixture.Placements);
            var caps=new List<PlatformCapability>{new PlatformCapability(PlatformFeature.Lifecycle,CapabilitySupport.Supported,CapabilityAvailability.Available,"fixture.lifecycle")};
            if(capability)caps.Add(new PlatformCapability(PlatformFeature.RewardedAds,CapabilitySupport.Supported,CapabilityAvailability.Available,"fixture.ads",CapabilityConstraints.ForegroundOnly|CapabilityConstraints.UserGestureRequired,1));
            PlatformBackend.Inner.Startup=new PlatformStartup(new PlatformCapabilities(caps),PlatformVisibility.Foreground);
            var platform=new PlatformService(PlatformBackend);Ads=new AdsService(Backend,Journal);
            Context=new GameContext(new[]{new ModuleRegistration("ads",Ads.Dependencies,()=>Ads,typeof(IAdsService)),new ModuleRegistration("platform",platform.Dependencies,()=>platform,typeof(IPlatformCapabilities),typeof(IPlatformLifecycle))},Cleanup);
        }
        public IAdOperation Capture(string claim="claim-a"){var r=Ads.Capture(new RewardedAdRequest("revive",claim,Owner,true));Assert.IsTrue(r.IsSuccess);return r.Value;}
    }
    readonly List<Env> envs=new List<Env>();
    async UniTask<Env> New(bool cap=true){var e=new Env(cap);envs.Add(e);Check(await e.Context.StartAsync(Wait));return e;}
    static void Check(Result r)=>Assert.IsTrue(r.IsSuccess,r.IsSuccess?"":r.Error.Code);
    static async UniTask Frames(int count=6){for(int i=0;i<count;i++)await UniTask.Yield();}
    [UnityTearDown]public IEnumerator Teardown()=>UniTask.ToCoroutine(async()=>{
        foreach(var e in envs){foreach(var h in e.Api.Handles){h.FailRelease=h.ThrowRelease=false;h.CompleteLoad();h.CompleteAck();h.Close(false);h.CompleteRelease();}await Frames();var close=e.Context.CloseAsync(Wait).Preserve();for(int i=0;i<8;i++){foreach(var h in e.Api.Handles){h.CompleteLoad();h.CompleteAck();h.Close(false);h.CompleteRelease();}await UniTask.Yield();}var r=await close;if(!r.IsSuccess)Check(await e.Context.RetryCleanupAsync(Cleanup,Wait));Assert.AreEqual(0,e.Context.RetainedModuleCount);Assert.AreEqual(0,e.Backend.RetainedAttempts);}envs.Clear();});
    [UnityTest]public IEnumerator CompletedFactKeepsOwnerAndMockProvenance()=>UniTask.ToCoroutine(async()=>{var e=await New();e.Api.Configure=h=>h.CloseDuringAck=true;var op=e.Capture();var r=await op.WaitAsync(Wait);Assert.IsTrue(r.IsSuccess);Assert.IsTrue(r.Value.Owner.SameAs(Owner));Assert.AreEqual(op.AttemptId,r.Value.AttemptId);Assert.AreEqual(AdWatchOutcome.Completed,r.Value.Outcome);Assert.AreEqual(PlatformExecutionMode.Mock,r.Value.Mode);Assert.AreEqual(1,e.Journal.PendingCount);Assert.AreEqual(0,e.Backend.RetainedAttempts);Assert.AreEqual("adunit-synthetic-fixture",e.Api.Handles[0].Unit);});
    [UnityTest]public IEnumerator CancellationAfterShowDoesNotReleaseSlotForNextAttempt()=>UniTask.ToCoroutine(async()=>{var e=await New();using(var cts=new CancellationTokenSource()){var first=e.Capture();var observed=first.WaitAsync(new OperationContext(cts.Token,Timeout.InfiniteTimeSpan)).Preserve();await Frames();Assert.AreEqual(1,e.Api.Handles[0].Shows);cts.Cancel();await Frames();Assert.AreEqual(ErrorKind.Canceled,(await observed).Error.Kind);var second=e.Capture("claim-b");var next=second.WaitAsync(Wait).Preserve();await Frames();Assert.AreEqual(1,e.Api.Creates);Assert.AreEqual(0,e.Api.Handles[0].Releases);e.Api.Handles[0].Close(true);await Frames(10);Assert.AreEqual(2,e.Api.Creates);e.Api.Handles[1].Close(false);Assert.IsTrue((await next).IsSuccess);Assert.AreEqual(2,e.Journal.PendingCount);Assert.IsTrue(e.Journal.Pending[0].Owner.SameAs(Owner));}});
    [UnityTest]public IEnumerator ObservationTimeoutStillWaitsForNativeTerminal()=>UniTask.ToCoroutine(async()=>{var e=await New();var op=e.Capture();var observed=op.WaitAsync(new OperationContext(default,TimeSpan.FromMilliseconds(300))).Preserve();await Frames(2);Assert.AreEqual(1,e.Api.Handles[0].Shows);await UniTask.Delay(350,ignoreTimeScale:true);Assert.AreEqual(ErrorKind.Timeout,(await observed).Error.Kind);Assert.AreEqual(1,e.Backend.ActiveAttemptCount);Assert.AreEqual(0,e.Api.Handles[0].Releases);e.Api.Handles[0].Close(true);await Frames();Assert.AreEqual(1,e.Journal.PendingCount);Assert.AreEqual(0,e.Backend.ActiveAttemptCount);});
    [UnityTest]public IEnumerator ContextCloseDrainsFactBeforeReleasingPlatform()=>UniTask.ToCoroutine(async()=>{var e=await New();var observed=e.Capture().WaitAsync(Wait).Preserve();await Frames();var close=e.Context.CloseAsync(Wait).Preserve();await Frames();Assert.AreEqual(UniTaskStatus.Pending,close.Status);Assert.AreEqual(0,e.Api.Handles[0].Releases);e.Api.Handles[0].Close(true);Check(await close);Assert.AreEqual(0,e.Context.RetainedModuleCount);Assert.AreEqual(1,e.Journal.PendingCount);Assert.AreEqual(0,e.Backend.RetainedAttempts);var r=await observed;Assert.IsFalse(r.IsSuccess);});
    [UnityTest]public IEnumerator ReleaseFailureRetryNeverReplaysShowOrFact()=>UniTask.ToCoroutine(async()=>{var e=await New();e.Api.Configure=h=>{h.CloseDuringAck=true;h.FailRelease=true;};var op=e.Capture();var r=await op.WaitAsync(Wait);Assert.IsFalse(r.IsSuccess);Assert.AreEqual(AdPhase.PendingFinalization,op.Phase);Assert.AreEqual(1,e.Journal.PendingCount);Assert.AreEqual(1,e.Backend.ActiveAttemptCount);e.Api.Handles[0].FailRelease=false;Check(await e.Ads.RetryFinalizationAsync(Cleanup,Wait));Assert.AreEqual(1,e.Api.Creates);Assert.AreEqual(1,e.Api.Handles[0].Shows);Assert.AreEqual(2,e.Api.Handles[0].Releases);Assert.AreEqual(1,e.Journal.PendingCount);Assert.AreEqual(0,e.Backend.RetainedAttempts);});
    [UnityTest]public IEnumerator MissingCapabilityDoesNotEnterSdkPort()=>UniTask.ToCoroutine(async()=>{var e=await New(false);var r=e.Ads.Capture(new RewardedAdRequest("revive","claim-a",Owner,true));Assert.IsFalse(r.IsSuccess);Assert.AreEqual(0,e.Api.Creates);Assert.AreEqual(0,e.Backend.RetainedAttempts);});
}
