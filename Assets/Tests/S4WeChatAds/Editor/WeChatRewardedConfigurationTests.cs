using System;
using System.Collections.Generic;
using System.Threading;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat.Ads;
using MiniFramework.Tests.WeChatAds;
using NUnit.Framework;
public sealed class WeChatRewardedConfigurationTests
{
    static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);
    [Test] public void NullApiRejected()=>Assert.Throws<ArgumentNullException>(()=>new WeChatRewardedBackend(null,RewardedApiFixture.Placements));
    [Test] public void MissingConfigRejected()=>Assert.Throws<ArgumentException>(()=>new WeChatRewardedBackend(new RewardedApiFixture(),null));
    [Test] public void EmptyConfigRejected()=>Assert.Throws<ArgumentException>(()=>new WeChatRewardedBackend(new RewardedApiFixture(),new Dictionary<string,string>()));
    [TestCase("Bad","adunit-fixture")][TestCase("revive","")][TestCase("revive","not-ad-id")][TestCase("revive","adunit-")][TestCase("revive","adunit-private/path")]
    public void InvalidPlacementRejected(string key,string value)=>Assert.Throws<ArgumentException>(()=>new WeChatRewardedBackend(new RewardedApiFixture(),new Dictionary<string,string>{{key,value}}));
    [TestCase(0)][TestCase(1025)] public void AttemptLimitBounded(int max)=>Assert.Throws<ArgumentException>(()=>new WeChatRewardedBackend(new RewardedApiFixture(),RewardedApiFixture.Placements,max));
    [Test] public void TooManyPlacementsRejected(){var p=new Dictionary<string,string>();for(int i=0;i<33;i++)p.Add("p"+i,"adunit-fixture");Assert.Throws<ArgumentException>(()=>new WeChatRewardedBackend(new RewardedApiFixture(),p));}
    [Test] public void InvalidModeRejected(){var api=new RewardedApiFixture{Mode=(PlatformExecutionMode)99};Assert.Throws<ArgumentException>(()=>new WeChatRewardedBackend(api,RewardedApiFixture.Placements));}
    [Test] public void ConstructionAndCreateRemainCold(){var api=new RewardedApiFixture();var b=new WeChatRewardedBackend(api,RewardedApiFixture.Placements);var a=b.Create("g","a","revive",new CallbackSinkFixture());Assert.AreEqual(0,api.Creates);Assert.AreEqual(1,b.RetainedAttempts);Assert.IsTrue(a.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(0,b.RetainedAttempts);}
    [Test] public void PlacementMapIsCopied(){var api=new RewardedApiFixture();var p=new Dictionary<string,string>{{"revive","adunit-fixture"}};var b=new WeChatRewardedBackend(api,p);p.Clear();var a=b.Create("g","a","revive",new CallbackSinkFixture());Assert.IsTrue(a.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);}
    [TestCase("bad gen","a","revive")][TestCase("g","bad/attempt","revive")][TestCase("g","a","unknown")]
    public void InvalidAttemptNeverCallsApi(string gen,string id,string placement){var api=new RewardedApiFixture();var b=new WeChatRewardedBackend(api,RewardedApiFixture.Placements);Assert.Throws<ArgumentException>(()=>b.Create(gen,id,placement,new CallbackSinkFixture()));Assert.AreEqual(0,api.Creates);}
    [Test] public void NullSinkRejected(){var b=new WeChatRewardedBackend(new RewardedApiFixture(),RewardedApiFixture.Placements);Assert.Throws<ArgumentException>(()=>b.Create("g","a","revive",null));}
    [Test] public void BoundedRetentionReleasedByIdempotentCleanup(){var b=new WeChatRewardedBackend(new RewardedApiFixture(),RewardedApiFixture.Placements,1);var a=b.Create("g","a","revive",new CallbackSinkFixture());Assert.Throws<InvalidOperationException>(()=>b.Create("g","b","revive",new CallbackSinkFixture()));Assert.IsTrue(a.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.IsTrue(a.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);var c=b.Create("g","c","revive",new CallbackSinkFixture());Assert.AreEqual(1,b.RetainedAttempts);c.CleanupAsync(Cleanup).GetAwaiter().GetResult();}
    [Test] public void WorkerCreateRejected(){var b=new WeChatRewardedBackend(new RewardedApiFixture(),RewardedApiFixture.Placements);Exception caught=null;var thread=new Thread(()=>{try{b.Create("g","a","revive",new CallbackSinkFixture());}catch(Exception ex){caught=ex;}});thread.Start();thread.Join();Assert.IsInstanceOf<InvalidOperationException>(caught);Assert.AreEqual(0,b.RetainedAttempts);}
}
