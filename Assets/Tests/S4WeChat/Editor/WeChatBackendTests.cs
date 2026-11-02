using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat;
using MiniFramework.WeChat.Tests;
using NUnit.Framework;
public sealed class WeChatBackendTests
{
    static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
    sealed class Sink : IPlatformCallbackSink
    {
        internal readonly List<PlatformCallback> Values = new List<PlatformCallback>();
        internal PlatformCallback Last;
        internal bool Reject, Throw;
        public bool TryPost(PlatformCallback callback) { Last=callback;if (Throw) throw new InvalidOperationException(); if (Reject) return false; Values.Add(callback); return true; }
    }
    sealed class Env
    {
        internal readonly ScriptedWeChatApi Api = new ScriptedWeChatApi(); internal readonly Sink Sink = new Sink();
        internal readonly WeChatBackend Backend; internal readonly IPlatformSession Session; internal IPlatformLoginAttempt Attempt;
        internal Env() { Backend = new WeChatBackend(Api); Session = Backend.Open("test-generation", Sink); }
        internal Result<PlatformStartup> Init() => Session.InitializeAsync().GetAwaiter().GetResult();
        internal IPlatformLoginAttempt Create() => Attempt = Session.CreateLogin("test-attempt", new PlatformLoginOptions(true));
        internal ScriptedWeChatApi.Request Request => Api.Requests[Api.Requests.Count - 1];
        internal void Finish()
        {
            Api.FailOffShow = Api.FailOffHide = Api.FailRelease = false; Api.SavedInitialize?.Invoke(0);
            foreach (var r in Api.Requests) r.Complete();
            if (Attempt != null) Assert.IsTrue(Attempt.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);
            Assert.IsTrue(Session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);
            foreach (var c in Sink.Values) c.Discard();
        }
    }
    readonly List<Env> envs = new List<Env>(); Env New() { var e = new Env(); envs.Add(e); return e; }
    [TearDown] public void TearDown() { foreach (var e in envs) e.Finish(); envs.Clear(); }
    [Test] public void BackendAndOpenAreCold() { var e=New();Assert.AreEqual(0,e.Api.InitializeCalls);Assert.IsEmpty(e.Api.Shows); }
    [Test] public void DescriptorReflectsMockAndPinnedAdapterVersion() { var e=New();Assert.AreEqual("wechat",e.Backend.Descriptor.Id);Assert.AreEqual(PlatformExecutionMode.Mock,e.Backend.Descriptor.Mode);StringAssert.Contains("202610200817",e.Backend.Descriptor.AdapterVersion); }
    [Test] public void NullApiAndInvalidGenerationRejectBeforeSdk() { Assert.Throws<ArgumentNullException>(()=>new WeChatBackend(null));var e=New();Assert.Throws<ArgumentException>(()=>e.Backend.Open("",e.Sink));Assert.AreEqual(0,e.Api.InitializeCalls); }
    [Test] public void StartupVisibilityIsUnknownWithoutHostEvent() { var e=New();Assert.AreEqual(PlatformVisibility.Unknown,e.Init().Value.Visibility); }
    [Test] public void StartupDoesNotInventOtherCapabilities() { var e=New();var c=e.Init().Value.Capabilities;Assert.IsTrue(c.Get(PlatformFeature.LoginCredential).IsUsable);Assert.AreEqual(CapabilitySupport.Unknown,c.Get(PlatformFeature.RewardedAds).Support); }
    [Test] public void InitializationIsSingleUse() { var e=New();e.Init();Assert.AreEqual(ErrorKind.InvalidState,e.Session.InitializeAsync().GetAwaiter().GetResult().Error.Kind);Assert.AreEqual(1,e.Api.InitializeCalls); }
    [Test] public void InitializationFailureHasStableRedactedError() { var e=New();e.Api.InitializeCode=5;Assert.AreEqual("wechat.initialize.failed",e.Init().Error.Code); }
    [Test] public void PartialSubscriptionFailureIsOwnedAndUnbound() { var e=New();e.Api.ThrowShowAfterBind=true;Assert.IsFalse(e.Init().IsSuccess);Assert.AreEqual(0,e.Api.InitializeCalls);Assert.IsTrue(e.Session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.IsEmpty(e.Api.Shows); }
    [Test] public void InitializationThrowDoesNotInventActualCompletion() { var e=New();e.Api.ThrowInitialize=true;var t=e.Session.InitializeAsync();Assert.AreEqual(UniTaskStatus.Pending,t.Status);Assert.IsFalse(e.Session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);e.Api.SavedInitialize(0);Assert.IsTrue(t.GetAwaiter().GetResult().IsSuccess); }
    [Test] public void StartupSnapshotIncludesEventsDuringInitialization() { var e=New();e.Api.HoldInitialize=true;var t=e.Session.InitializeAsync();e.Api.Hide();e.Api.Show();e.Api.SavedInitialize(0);var s=t.GetAwaiter().GetResult().Value;Assert.AreEqual(2,s.Sequence);Assert.AreEqual(PlatformVisibility.Foreground,s.Visibility); }
    [Test] public void LifecycleUsesSingleOrderedGenerationStream() { var e=New();e.Init();e.Api.Hide();e.Api.Show();Assert.AreEqual(1,e.Sink.Values[0].Sequence);Assert.AreEqual(2,e.Sink.Values[1].Sequence);Assert.AreEqual("test-generation",e.Sink.Values[1].Generation); }
    [Test] public void CreateBeforeInitializationRejectsWithoutSdk() { var e=New();Assert.Throws<InvalidOperationException>(()=>e.Create());Assert.AreEqual(0,e.Api.CreateCalls); }
    [Test] public void CreateLoginIsCold() { var e=New();e.Init();e.Create();Assert.AreEqual(0,e.Api.CreateCalls);Assert.AreEqual(0,e.Api.StartCalls); }
    [Test] public void OnlyOneRequestIsRetainedPerSession() { var e=New();e.Init();e.Create();Assert.Throws<InvalidOperationException>(()=>e.Session.CreateLogin("second",default)); }
    [Test] public void RequestStartIsSingleUse() { var e=New();e.Init();var a=e.Create();Assert.IsTrue(a.StartAsync().GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(ErrorKind.InvalidState,a.StartAsync().GetAwaiter().GetResult().Error.Kind);Assert.AreEqual(1,e.Api.StartCalls); }
    [Test] public void SuccessDoesNotTerminateSdkAttempt() { var e=New();e.Init();e.Api.HoldLogin=true;var t=e.Create().StartAsync();Assert.AreEqual(UniTaskStatus.Pending,t.Status);Assert.IsFalse(e.Attempt.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(0,e.Request.ReleaseCalls); }
    [Test] public void CancelCannotFakeAnAbort() { var e=New();e.Init();e.Api.HoldLogin=true;var t=e.Create().StartAsync();Assert.IsTrue(e.Attempt.Cancel().IsSuccess);Assert.AreEqual(UniTaskStatus.Pending,t.Status);Assert.IsFalse(e.Request.ActuallyComplete); }
    [Test] public void CredentialIsAttemptBoundMockAndRedacted() { var e=New();e.Init();e.Create().StartAsync().GetAwaiter().GetResult();var c=e.Sink.Values[0].LoginResult.Value;Assert.AreEqual("test-attempt",c.AttemptId);Assert.AreEqual(PlatformExecutionMode.Mock,c.Mode);Assert.IsNull(c.ExpiresAtUtc);Assert.IsFalse(c.ToString().Contains("synthetic-code")); }
    [Test] public void DuplicateSuccessPublishesOnlyOneCredential() { var e=New();e.Init();e.Api.HoldLogin=true;e.Create().StartAsync();e.Request.Success("duplicate");Assert.AreEqual(1,e.Sink.Values.Count); }
    [Test] public void RejectedCredentialIsDisposedByProducer() { var e=New();e.Init();e.Sink.Reject=true;e.Create().StartAsync().GetAwaiter().GetResult();Assert.IsEmpty(e.Sink.Values);Assert.IsTrue(e.Sink.Last.LoginResult.Value.IsDisposed); }
    [Test] public void ThrowingSinkCannotEscapeSdkCallback() { var e=New();e.Init();e.Sink.Throw=true;Assert.DoesNotThrow(()=>e.Create().StartAsync().GetAwaiter().GetResult()); }
    [Test] public void EmptyCodeIsIntegrityFailure() { var e=New();e.Init();e.Api.AutoReply=false;e.Api.HoldLogin=true;e.Create().StartAsync();e.Request.Success("");Assert.AreEqual(ErrorKind.Integrity,e.Sink.Values[0].LoginResult.Error.Kind); }
    [Test] public void OversizedCodeIsIntegrityFailure() { var e=New();e.Init();e.Api.AutoReply=false;e.Api.HoldLogin=true;e.Create().StartAsync();e.Request.Success(new string('x',16385));Assert.AreEqual(ErrorKind.Integrity,e.Sink.Values[0].LoginResult.Error.Kind); }
    [Test] public void GeneralSdkFailureDoesNotGuessPermissionFromText() { var e=New();e.Init();e.Api.AutoReply=false;e.Api.HoldLogin=true;e.Create().StartAsync();e.Request.Fail();Assert.AreEqual(ErrorKind.Provider,e.Sink.Values[0].LoginResult.Error.Kind);Assert.IsEmpty(e.Sink.Values[0].LoginResult.Error.Message); }
    [Test] public void CompleteBeforeResultSealsLatePayload() { var e=New();e.Init();e.Api.AutoReply=false;e.Api.HoldLogin=true;var t=e.Create().StartAsync();e.Request.Complete();e.Request.Success();Assert.IsTrue(t.GetAwaiter().GetResult().IsSuccess);Assert.IsEmpty(e.Sink.Values); }
    [Test] public void DispatchThrowRetainsUnknownWorkUntilActualComplete() { var e=New();e.Init();e.Api.ThrowStart=true;var t=e.Create().StartAsync();Assert.AreEqual("wechat.login.dispatch_unknown",e.Sink.Values[0].LoginResult.Error.Code);Assert.AreEqual(UniTaskStatus.Pending,t.Status);e.Request.Complete();Assert.IsTrue(t.GetAwaiter().GetResult().IsSuccess); }
    [Test] public void ColdConstructionThrowIsKnownRejected() { var e=New();e.Init();e.Api.ThrowCreate=true;Assert.AreEqual("wechat.login.create_failed",e.Create().StartAsync().GetAwaiter().GetResult().Error.Code); }
    [Test] public void NullColdRequestIsKnownRejected() { var e=New();e.Init();e.Api.NullRequest=true;Assert.AreEqual("wechat.login.null_request",e.Create().StartAsync().GetAwaiter().GetResult().Error.Code); }
    [Test] public void ReleaseFailureRetriesOnlyRelease() { var e=New();e.Init();e.Create().StartAsync().GetAwaiter().GetResult();e.Api.FailRelease=true;Assert.IsFalse(e.Attempt.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);e.Api.FailRelease=false;Assert.IsTrue(e.Attempt.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(1,e.Api.StartCalls);Assert.AreEqual(2,e.Request.ReleaseCalls); }
    [Test] public void DetachFailureStillAttemptsOtherSubscription() { var e=New();e.Init();e.Api.FailOffShow=true;Assert.IsFalse(e.Session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.IsEmpty(e.Api.Hides);Assert.AreEqual(1,e.Api.OffHideCalls);e.Api.FailOffShow=false;Assert.IsTrue(e.Session.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(1,e.Api.OffHideCalls); }
    [Test] public void ClosedSessionUnsubscribesAndCannotCreateLogin() { var e=New();e.Init();e.Session.CleanupAsync(Cleanup).GetAwaiter().GetResult();e.Api.Show();Assert.IsEmpty(e.Sink.Values);Assert.Throws<InvalidOperationException>(()=>e.Create()); }
    [Test] public void UnstartedAttemptCanBeReleasedWithoutSdkCall() { var e=New();e.Init();e.Create().CleanupAsync(Cleanup).GetAwaiter().GetResult();Assert.AreEqual(0,e.Api.CreateCalls); }
}
