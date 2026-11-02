using System;
using System.Collections.Generic;
using System.Threading;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat.Ads;
using MiniFramework.Providers.WeChat.WxSdk;
using MiniFramework.Providers.WeChat.WxRewarded;
using NUnit.Framework;
using WeChatWASM;
public sealed class NativeRewardedTests
{
    static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
    sealed class Resource : IWxRewardedObject
    {
        public int Loads, Shows, Destroys, OffCloses, OffErrors;
        public bool ThrowSubscribe, ThrowOffClose, ThrowOffError, ThrowDestroy, ThrowLoad, ThrowShow;
        public Action<WXRewardedVideoAdOnCloseResponse> Closed;
        public Action<WXADErrorResponse> Error, LoadFail;
        public Action<WXTextResponse> Loaded, Shown, ShowFail;
        public void OnClose(Action<WXRewardedVideoAdOnCloseResponse> f) { Closed = f; if (ThrowSubscribe) throw new Exception(); }
        public void OffClose(Action<WXRewardedVideoAdOnCloseResponse> f) { OffCloses++; if (ThrowOffClose) throw new Exception(); if (Closed == f) Closed = null; }
        public void OnError(Action<WXADErrorResponse> f) { Error = f; }
        public void OffError(Action<WXADErrorResponse> f) { OffErrors++; if (ThrowOffError) throw new Exception(); if (Error == f) Error = null; }
        public void Load(Action<WXTextResponse> s, Action<WXADErrorResponse> f) { Loads++; Loaded = s; LoadFail = f; if (ThrowLoad) throw new Exception(); }
        public void Show(Action<WXTextResponse> s, Action<WXTextResponse> f) { Shows++; Shown = s; ShowFail = f; if (ThrowShow) throw new Exception(); }
        public void Destroy() { Destroys++; if (ThrowDestroy) throw new Exception(); }
    }
    sealed class Env
    {
        public readonly Queue<Action> Queue = new Queue<Action>();
        public readonly Resource R = new Resource();
        public readonly WxRewardedApi Api;
        public readonly IWeChatRewardedHandle H;
        public int Creates, Closed, Errors;
        public bool? Ended;
        public FrameworkError LastError;
        public Env() { Api = new WxRewardedApi(_ => { Creates++; return R; }, a => Queue.Enqueue(a)); H = Api.Create("adunit-controlled-fixture", b => { Closed++; Ended = b; }, e => { Errors++; LastError = e; }); }
        public void Drain() { int limit = 16; while (Queue.Count > 0) { Assert.Greater(limit--, 0); Queue.Dequeue()(); } }
        public void Load() { var t = H.LoadAsync(); R.Loaded(new WXTextResponse()); Drain(); Assert.IsTrue(t.GetAwaiter().GetResult().IsSuccess); }
        public void Show() { H.ShowAsync(); R.Shown(new WXTextResponse()); Drain(); }
        public Result Release() => H.ReleaseAsync(Cleanup).GetAwaiter().GetResult();
    }
    [Test] public void NativeFactoryRejectsEditorSimulation() { Assert.AreEqual(ErrorKind.NotSupported, WxRewardedApi.CreateNativeApi().Error.Kind); }
    [Test] public void ControlledCSharpResourceCannotClaimNative() { Assert.AreEqual(PlatformExecutionMode.Mock, new Env().Api.Mode); }
    [TestCase(false,false,false,CapabilityAvailability.Unavailable)]
    [TestCase(true,false,false,CapabilityAvailability.Unavailable)]
    [TestCase(true,true,false,CapabilityAvailability.PermissionDenied)]
    [TestCase(true,true,true,CapabilityAvailability.Available)]
    public void CapabilityRequiresHostConfigurationAndPermission(bool host,bool config,bool permission,CapabilityAvailability expected) { var c=WxRewardedApi.ClassifyCapability(host,config,permission); Assert.AreEqual(expected,c.Availability); Assert.AreEqual(host&&config&&permission,c.IsUsable); Assert.AreEqual(1,c.MaxConcurrent); }
    [TestCase(1000,ErrorKind.Provider)][TestCase(1001,ErrorKind.InvalidArgument)][TestCase(1002,ErrorKind.InvalidArgument)]
    [TestCase(1003,ErrorKind.Provider)][TestCase(1004,ErrorKind.NoFill)][TestCase(1005,ErrorKind.Unavailable)]
    [TestCase(1006,ErrorKind.PermissionDenied)][TestCase(1007,ErrorKind.PermissionDenied)][TestCase(1008,ErrorKind.PermissionDenied)]
    [TestCase(1009,ErrorKind.Provider)][TestCase(-1,ErrorKind.Provider)][TestCase(0,ErrorKind.Provider)]
    public void DocumentedCodesOnly(int code,ErrorKind expected) { var r=WxRewardedApi.Classify(code); Assert.AreEqual(expected,r.Kind); Assert.AreEqual("",r.Message); Assert.AreEqual(RetryHint.Never,r.Retry); }
    [Test] public void FactoryAndColdReleaseNeverEnterSdk() { var e=new Env();Assert.IsTrue(e.Release().IsSuccess);Assert.AreEqual(0,e.Creates);Assert.IsFalse(e.H.LoadAsync().GetAwaiter().GetResult().IsSuccess); }
    [Test] public void LoadResultWaitsForSdkCallbackDispatchReturn() { var e=new Env();var t=e.H.LoadAsync().Preserve();e.R.Loaded(new WXTextResponse());Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending,t.Status);Assert.IsFalse(e.Release().IsSuccess);e.Drain();Assert.IsTrue(t.GetAwaiter().GetResult().IsSuccess);Assert.IsTrue(e.Release().IsSuccess); }
    [Test] public void ShowAckDoesNotCloseVisibleResource() {var e=new Env();e.Load();e.Show();Assert.AreEqual(0,e.Closed);Assert.IsFalse(e.Release().IsSuccess);e.R.Closed(new WXRewardedVideoAdOnCloseResponse{isEnded=true});e.Drain();Assert.IsTrue(e.Release().IsSuccess);Assert.AreEqual(true,e.Ended);}
    [Test] public void NullCloseIsNeverCompletion() {var e=new Env();e.Load();e.Show();e.R.Closed(null);e.Drain();Assert.AreEqual(false,e.Ended);Assert.IsTrue(e.Release().IsSuccess);}
    [Test] public void DefaultBooleanIsNeverCompletion() {var e=new Env();e.Load();e.Show();e.R.Closed(new WXRewardedVideoAdOnCloseResponse());e.Drain();Assert.AreEqual(false,e.Ended);Assert.IsTrue(e.Release().IsSuccess);}
    [Test] public void FirstCloseWinsAndLateErrorIgnored() {var e=new Env();e.Load();e.Show();e.R.Closed(new WXRewardedVideoAdOnCloseResponse{isEnded=false});e.R.Closed(new WXRewardedVideoAdOnCloseResponse{isEnded=true});e.R.Error(new WXADErrorResponse{errCode=1004});e.Drain();Assert.AreEqual(1,e.Closed);Assert.AreEqual(false,e.Ended);Assert.AreEqual(0,e.Errors);Assert.IsTrue(e.Release().IsSuccess);}
    [Test] public void FirstErrorWinsAndLateCloseIgnored() {var e=new Env();e.Load();e.Show();e.R.Error(new WXADErrorResponse{errCode=1004});e.R.Closed(new WXRewardedVideoAdOnCloseResponse{isEnded=true});e.Drain();Assert.AreEqual(ErrorKind.NoFill,e.LastError.Kind);Assert.AreEqual(0,e.Closed);Assert.IsTrue(e.Release().IsSuccess);}
    [Test] public void ErrorEventDoesNotInventLoadCommandCompletion() {var e=new Env();var t=e.H.LoadAsync().Preserve();e.R.Error(new WXADErrorResponse{errCode=1007});e.Drain();Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending,t.Status);Assert.IsFalse(e.Release().IsSuccess);e.R.LoadFail(new WXADErrorResponse{errCode=1007});e.Drain();Assert.AreEqual(ErrorKind.PermissionDenied,t.GetAwaiter().GetResult().Error.Kind);Assert.IsTrue(e.Release().IsSuccess);}
    [Test] public void ErrorBetweenLoadAndShowPreventsShow() {var e=new Env();e.Load();e.R.Error(new WXADErrorResponse{errCode=1004});var t=e.H.ShowAsync();Assert.AreEqual(ErrorKind.NoFill,t.GetAwaiter().GetResult().Error.Kind);Assert.AreEqual(0,e.R.Shows);e.Drain();Assert.IsTrue(e.Release().IsSuccess);}
    [Test] public void DuplicateLoadAndShowCallbacksCoalesce() {var e=new Env();var l=e.H.LoadAsync();e.R.Loaded(new WXTextResponse());e.R.Loaded(new WXTextResponse());Assert.AreEqual(1,e.Queue.Count);e.Drain();Assert.IsTrue(l.GetAwaiter().GetResult().IsSuccess);var s=e.H.ShowAsync();e.R.ShowFail(new WXTextResponse{errCode=1001});e.R.Shown(new WXTextResponse());Assert.AreEqual(1,e.Queue.Count);e.Drain();Assert.AreEqual(ErrorKind.InvalidArgument,s.GetAwaiter().GetResult().Error.Kind);Assert.IsTrue(e.Release().IsSuccess);}
    [Test] public void CancelLoadKeepsActualCallbackAndSdkLease() {var e=new Env();var t=e.H.LoadAsync().Preserve();Assert.IsTrue(e.H.CancelLoad().IsSuccess);Assert.IsTrue(e.H.CancelLoad().IsSuccess);Assert.IsFalse(e.Release().IsSuccess);e.R.Loaded(new WXTextResponse());e.Drain();Assert.IsTrue(t.GetAwaiter().GetResult().IsSuccess);Assert.IsFalse(e.H.ShowAsync().GetAwaiter().GetResult().IsSuccess);Assert.IsTrue(e.Release().IsSuccess);Assert.AreEqual(1,e.R.Loads);}
    [Test] public void ReleaseRetryOnlyRepeatsFailedPhase() {var e=new Env();e.Load();e.R.ThrowOffError=true;Assert.IsFalse(e.Release().IsSuccess);Assert.AreEqual(1,e.Api.RetainedResources);Assert.AreEqual(1,e.R.OffCloses);Assert.AreEqual(0,e.R.Destroys);e.R.ThrowOffError=false;Assert.IsTrue(e.Release().IsSuccess);Assert.AreEqual(1,e.R.OffCloses);Assert.AreEqual(2,e.R.OffErrors);Assert.AreEqual(1,e.R.Destroys);Assert.AreEqual(1,e.R.Loads);}
    [Test] public void DestroyFailureRetainsLeaseAndDoesNotResubscribeOrReplay() {var e=new Env();e.Load();e.R.ThrowDestroy=true;Assert.IsFalse(e.Release().IsSuccess);e.R.ThrowDestroy=false;Assert.IsTrue(e.Release().IsSuccess);Assert.AreEqual(1,e.R.OffCloses);Assert.AreEqual(1,e.R.OffErrors);Assert.AreEqual(2,e.R.Destroys);Assert.AreEqual(1,e.R.Loads);Assert.AreEqual(0,e.Api.RetainedResources);}
    [Test] public void PartialSubscriptionRemainsOwnedForExplicitCleanup() {var e=new Env();e.R.ThrowSubscribe=true;Assert.IsFalse(e.H.LoadAsync().GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(1,e.Api.RetainedResources);Assert.IsTrue(e.Release().IsSuccess);Assert.AreEqual(1,e.R.OffCloses);Assert.AreEqual(1,e.R.OffErrors);Assert.AreEqual(0,e.R.Loads);}
    [Test] public void OldCopiedCallbacksCannotReachConsumerAfterRelease() {var e=new Env();e.Load();e.Show();var close=e.R.Closed;var error=e.R.Error;close(new WXRewardedVideoAdOnCloseResponse());e.Drain();Assert.IsTrue(e.Release().IsSuccess);close(new WXRewardedVideoAdOnCloseResponse{isEnded=true});error(new WXADErrorResponse{errCode=1004});e.Drain();Assert.AreEqual(1,e.Closed);Assert.AreEqual(0,e.Errors);}
    [Test] public void EarlyCleanupDoesNotSuppressActualClose() {var e=new Env();e.Load();e.Show();Assert.IsFalse(e.Release().IsSuccess);e.R.Closed(new WXRewardedVideoAdOnCloseResponse());e.Drain();Assert.AreEqual(1,e.Closed);Assert.IsTrue(e.Release().IsSuccess);}
    [Test] public void CloseBeforeShowAckStillWaitsForAckDispatch() {var e=new Env();e.Load();var t=e.H.ShowAsync().Preserve();e.R.Closed(new WXRewardedVideoAdOnCloseResponse{isEnded=true});e.Drain();Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending,t.Status);Assert.IsFalse(e.Release().IsSuccess);e.R.Shown(new WXTextResponse());e.Drain();Assert.IsTrue(t.GetAwaiter().GetResult().IsSuccess);Assert.IsTrue(e.Release().IsSuccess);}
    [Test] public void LoadThrowCannotFabricateActualEndButLateCallbackCanDrain() {var e=new Env();e.R.ThrowLoad=true;var t=e.H.LoadAsync().Preserve();Assert.AreEqual(Cysharp.Threading.Tasks.UniTaskStatus.Pending,t.Status);Assert.IsFalse(e.Release().IsSuccess);e.R.LoadFail(new WXADErrorResponse());e.Drain();Assert.IsFalse(t.GetAwaiter().GetResult().IsSuccess);Assert.IsTrue(e.Release().IsSuccess);}
    [Test] public void ShowThrowKeepsLeaseUntilActualCallbackAndClose() {var e=new Env();e.Load();e.R.ThrowShow=true;var t=e.H.ShowAsync().Preserve();Assert.IsFalse(e.Release().IsSuccess);e.R.Shown(new WXTextResponse());e.R.Closed(new WXRewardedVideoAdOnCloseResponse());e.Drain();Assert.IsTrue(t.GetAwaiter().GetResult().IsSuccess);Assert.IsTrue(e.Release().IsSuccess);}
    [Test] public void FailedCreateIsQuarantinedWithoutAutomaticRetry() {var a=new WxRewardedApi(_=>throw new Exception(),_=>{});var h=a.Create("adunit-controlled-fixture",_=>{},_=>{});Assert.IsFalse(h.LoadAsync().GetAwaiter().GetResult().IsSuccess);Assert.IsFalse(h.ReleaseAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(1,a.RetainedResources);}
    [Test] public void ActualResourceCapacityIsReleasedOnlyAfterDestroy() {var e=new Env();e.Load();var next=e.Api.Create("adunit-controlled-fixture",_=>{},_=>{});Assert.IsFalse(next.LoadAsync().GetAwaiter().GetResult().IsSuccess);Assert.IsTrue(next.ReleaseAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(1,e.Api.RetainedResources);Assert.IsTrue(e.Release().IsSuccess);}
    [Test] public void HostQualificationRejectsUnknownOrMalformedEvidence() {Assert.IsTrue(WxSdkApi.IsQualifiedHost("ios","3.17.5"));Assert.IsFalse(WxSdkApi.IsQualifiedHost("browser","3.17.5"));Assert.IsFalse(WxSdkApi.IsQualifiedHost("ios","unknown"));Assert.IsFalse(WxSdkApi.IsQualifiedHost("devtools","2.7.9"));}
    [Test] public void WorkerFactoryCannotEnterSdk() {var e=new Env();Exception caught=null;var t=new Thread(()=>{try{e.Api.Create("adunit-controlled-fixture",_=>{},_=>{});}catch(Exception ex){caught=ex;}});t.Start();t.Join();Assert.IsInstanceOf<InvalidOperationException>(caught);Assert.AreEqual(0,e.Creates);}
    sealed class Sink : IAdCallbackSink
    {
        public readonly List<AdCallbackKind> Facts = new List<AdCallbackKind>();
        public bool TryPost(AdCallback callback) { Facts.Add(callback.Kind); return true; }
    }
    static WeChatRewardedBackend Bridge(Env e) => new WeChatRewardedBackend(e.Api, new Dictionary<string,string> { ["revive"] = "adunit-controlled-fixture" });
    [Test] public void TypedSdkLoadFailureCrossesCommonBridgeWithoutCreatingFacts()
    {
        var e=new Env();var sink=new Sink();var b=Bridge(e);var a=b.Create("native-gen","native-attempt","revive",sink);
        var loaded=a.LoadAsync();e.R.LoadFail(new WXADErrorResponse { errCode=1004 });e.Drain();
        Assert.AreEqual(ErrorKind.NoFill,loaded.GetAwaiter().GetResult().Error.Kind);Assert.AreEqual(0,sink.Facts.Count);
        Assert.IsTrue(a.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(0,b.RetainedAttempts);Assert.AreEqual(0,e.Api.RetainedResources);
    }
    [Test] public void TypedSdkCloseBeforeAckCrossesBridgeOnceAndReleasesBothLeases()
    {
        var e=new Env();var sink=new Sink();var b=Bridge(e);var a=b.Create("native-gen","native-attempt","revive",sink);
        var loaded=a.LoadAsync();e.R.Loaded(new WXTextResponse());e.Drain();Assert.IsTrue(loaded.GetAwaiter().GetResult().IsSuccess);
        var shown=a.ShowAsync();e.R.Closed(new WXRewardedVideoAdOnCloseResponse { isEnded=true });e.R.Shown(new WXTextResponse());e.Drain();
        Assert.IsTrue(shown.GetAwaiter().GetResult().IsSuccess);CollectionAssert.AreEqual(new[]{AdCallbackKind.Started,AdCallbackKind.Completed,AdCallbackKind.Closed},sink.Facts);
        Assert.IsTrue(a.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(0,b.RetainedAttempts);Assert.AreEqual(0,e.Api.RetainedResources);Assert.AreEqual(1,e.R.Shows);
    }
    [Test] public void TypedSdkDestroyFailureKeepsCommonBridgeLeaseUntilExplicitCleanupRetry()
    {
        var e=new Env();var b=Bridge(e);var a=b.Create("native-gen","native-attempt","revive",new Sink());
        var loaded=a.LoadAsync();e.R.Loaded(new WXTextResponse());e.Drain();Assert.IsTrue(loaded.GetAwaiter().GetResult().IsSuccess);e.R.ThrowDestroy=true;
        Assert.IsFalse(a.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(1,b.RetainedAttempts);Assert.AreEqual(1,e.Api.RetainedResources);
        e.R.ThrowDestroy=false;Assert.IsTrue(a.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(0,b.RetainedAttempts);Assert.AreEqual(0,e.Api.RetainedResources);Assert.AreEqual(1,e.R.Loads);Assert.AreEqual(0,e.R.Shows);
    }
}
