using System;
using System.Collections.Generic;
using System.Threading;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat;
using MiniFramework.WeChat.Tests;
using NUnit.Framework;
namespace MiniFramework.S407Memory.Tests
{
    public sealed class MemoryBackendTests
    {
        static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
        sealed class Sink : IPlatformCallbackSink { public readonly List<PlatformCallback> Events = new List<PlatformCallback>(); public bool TryPost(PlatformCallback value) { Events.Add(value); return true; } }
        [Test] public void ExistingApiExplicitlyDeclaresMemoryUnsupported()
        { var session = new WeChatBackend(new ScriptedWeChatApi()).Open("legacy",new Sink()); var ready=session.InitializeAsync().GetAwaiter().GetResult();Assert.AreEqual(CapabilitySupport.Unsupported,ready.Value.Capabilities.Get(PlatformFeature.MemoryWarning).Support);Assert.IsTrue(session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess); }
        [Test] public void NewFeatureDoesNotRenumberExistingFeatures()
        { Assert.AreEqual(0,(int)PlatformFeature.Lifecycle);Assert.AreEqual(6,(int)PlatformFeature.Haptics);Assert.AreEqual(7,(int)PlatformFeature.MemoryWarning); }
        [Test] public void OpenIsColdAndSuccessfulInitializationDeclaresMemoryCapability()
        {var api=new MemoryApi();var session=new WeChatBackend(api).Open("cold",new Sink());Assert.AreEqual(0,api.OnCalls);var ready=session.InitializeAsync().GetAwaiter().GetResult();Assert.IsTrue(ready.Value.Capabilities.Get(PlatformFeature.MemoryWarning).IsUsable);Assert.AreEqual(1,api.OnCalls);Assert.IsTrue(session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.IsEmpty(api.Memory);}
        [Test] public void MemoryIsOrderedWithoutChangingVisibility()
        {var api=new MemoryApi();var sink=new Sink();var session=new WeChatBackend(api).Open("ordered",sink);session.InitializeAsync().GetAwaiter().GetResult();api.Inner.Hide();api.Raise();api.Inner.Show();Assert.AreEqual(3,sink.Events.Count);for(int i=0;i<3;i++){Assert.AreEqual(i+1,sink.Events[i].Sequence);Assert.AreEqual("ordered",sink.Events[i].Generation);}Assert.AreEqual(PlatformLifecycleKind.MemoryWarning,sink.Events[1].Lifecycle);Assert.IsTrue(session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);}
        [Test] public void MemoryBeforeInitializationCannotPublishReadyWork()
        {var api=new MemoryApi();api.Inner.HoldInitialize=true;var sink=new Sink();var session=new WeChatBackend(api).Open("initializing",sink);var pending=session.InitializeAsync();api.Raise();Assert.IsEmpty(sink.Events);api.Inner.SavedInitialize(0);Assert.IsTrue(pending.GetAwaiter().GetResult().IsSuccess);Assert.IsTrue(session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);}
        [Test] public void SubscriptionThrowAfterEffectRemainsOwnedForRollback()
        {var api=new MemoryApi{FailOnAfter=true};var session=new WeChatBackend(api).Open("rollback",new Sink());Assert.IsFalse(session.InitializeAsync().GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(0,api.Inner.InitializeCalls);Assert.IsTrue(session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.IsEmpty(api.Inner.Shows);Assert.IsEmpty(api.Inner.Hides);Assert.IsEmpty(api.Memory);}
        [Test] public void FailedDetachRetainsOnlyMemoryAndFencesCallbacksUntilRetry()
        {var api=new MemoryApi();var sink=new Sink();var session=new WeChatBackend(api).Open("retry",sink);session.InitializeAsync().GetAwaiter().GetResult();var stale=api.Memory[0];api.FailOff=true;Assert.IsFalse(session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);stale();Assert.IsEmpty(sink.Events);Assert.IsEmpty(api.Inner.Shows);Assert.IsEmpty(api.Inner.Hides);Assert.AreEqual(1,api.Memory.Count);api.FailOff=false;Assert.IsTrue(session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(2,api.OffCalls);Assert.AreEqual(1,api.Inner.OffShowCalls);Assert.AreEqual(1,api.Inner.InitializeCalls);stale();Assert.IsEmpty(sink.Events);}
        [Test] public void ThrowAfterDetachCanBeRetriedIdempotently()
        {var api=new MemoryApi();var session=new WeChatBackend(api).Open("after",new Sink());session.InitializeAsync().GetAwaiter().GetResult();api.FailOffAfter=true;Assert.IsFalse(session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.IsEmpty(api.Memory);api.FailOffAfter=false;Assert.IsTrue(session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(1,api.OnCalls);}
        [Test] public void SharedApiKeepsOtherSessionSubscription()
        {var api=new MemoryApi();var a=new Sink();var b=new Sink();var sa=new WeChatBackend(api).Open("a",a);var sb=new WeChatBackend(api).Open("b",b);sa.InitializeAsync().GetAwaiter().GetResult();sb.InitializeAsync().GetAwaiter().GetResult();var stale=api.Memory[0];Assert.IsTrue(sa.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);stale();api.Raise();Assert.IsEmpty(a.Events);Assert.AreEqual(1,b.Events.Count);Assert.AreEqual("b",b.Events[0].Generation);Assert.IsTrue(sb.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);}
    }
}
