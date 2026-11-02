using System;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat.Ads;
using MiniFramework.Providers.WeChat.WxSdk;
using WeChatWASM;

namespace MiniFramework.Providers.WeChat.WxRewarded
{
    // Resource seam mirrors the SDK C# surface. Controlled resources are always Mock.
    internal interface IWxRewardedObject
    {
        void OnClose(Action<WXRewardedVideoAdOnCloseResponse> callback);
        void OffClose(Action<WXRewardedVideoAdOnCloseResponse> callback);
        void OnError(Action<WXADErrorResponse> callback);
        void OffError(Action<WXADErrorResponse> callback);
        void Load(Action<WXTextResponse> success, Action<WXADErrorResponse> failure);
        void Show(Action<WXTextResponse> success, Action<WXTextResponse> failure);
        void Destroy();
    }
    public sealed class WxRewardedApi : IWeChatRewardedApi
    {
        readonly Func<string, IWxRewardedObject> factory;
        readonly Action<Action> defer;
        Handle active;
        static Handle nativeActive;
        public PlatformExecutionMode Mode { get; }
        public int RetainedResources => active == null ? 0 : 1;
        WxRewardedApi(Func<string, IWxRewardedObject> factory, Action<Action> defer, PlatformExecutionMode mode)
        { this.factory = factory; this.defer = defer; Mode = mode; }
        internal WxRewardedApi(Func<string, IWxRewardedObject> controlled, Action<Action> defer)
            : this(controlled ?? throw new ArgumentNullException(nameof(controlled)), defer ?? throw new ArgumentNullException(nameof(defer)), PlatformExecutionMode.Mock) { }
        public static Result<IWeChatRewardedApi> CreateNativeApi()
        {
            if (!PlayerLoopHelper.IsMainThread) return Failure<IWeChatRewardedApi>(ErrorKind.InvalidState, "wechat.ads.main_thread");
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                var host = WxSdkApi.CreateNativeApi();
                if (host.IsSuccess && WX.CanIUse("CreateRewardedVideoAd"))
                    return Result<IWeChatRewardedApi>.Success(new WxRewardedApi(unit => new SdkObject(WX.CreateRewardedVideoAd(new WXCreateRewardedVideoAdParam { adUnitId = unit, multiton = true })),
                        action => PlayerLoopHelper.AddContinuation(PlayerLoopTiming.LastPostLateUpdate, action), PlatformExecutionMode.Native));
            }
            catch (Exception) { return Failure<IWeChatRewardedApi>(ErrorKind.Provider, "wechat.ads.capability_failed"); }
#endif
            return Failure<IWeChatRewardedApi>(ErrorKind.NotSupported, "wechat.ads.native_host_required");
        }
        public static PlatformCapability Capability(bool configured, bool displayPermissionConfirmed) =>
            ClassifyCapability(CreateNativeApi().IsSuccess, configured, displayPermissionConfirmed);
        internal static PlatformCapability ClassifyCapability(bool supported, bool configured, bool permitted) =>
            new PlatformCapability(PlatformFeature.RewardedAds, supported ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                !supported || !configured ? CapabilityAvailability.Unavailable : permitted ? CapabilityAvailability.Available : CapabilityAvailability.PermissionDenied,
                !supported ? "wechat.ads.host_required" : !configured ? "wechat.ads.configuration_required" : !permitted ? "wechat.ads.permission_required" : "wechat.ads.configured",
                CapabilityConstraints.ForegroundOnly | CapabilityConstraints.UserGestureRequired, 1);
        static Result<T> Failure<T>(ErrorKind kind, string code) => Result<T>.Failure(new FrameworkError(kind, code));
        internal static FrameworkError Classify(int code)
        {
            // Tencent official API typings. No errMsg/errno guessing; no implicit retries.
            switch (code)
            {
                case 1001: case 1002: return new FrameworkError(ErrorKind.InvalidArgument, "wechat.ads.configuration");
                case 1004: return new FrameworkError(ErrorKind.NoFill, "wechat.ads.no_fill");
                case 1005: return new FrameworkError(ErrorKind.Unavailable, "wechat.ads.unavailable");
                case 1006: case 1007: case 1008: return new FrameworkError(ErrorKind.PermissionDenied, "wechat.ads.permission");
                default: return new FrameworkError(ErrorKind.Provider, "wechat.ads.native");
            }
        }
        public IWeChatRewardedHandle Create(string adUnitId, Action<bool?> close, Action<FrameworkError> error)
        {
            if (!PlayerLoopHelper.IsMainThread) throw new InvalidOperationException("wechat.ads.main_thread");
            if (!ClaimOwnerSnapshot.IsToken(adUnitId) || !adUnitId.StartsWith("adunit-", StringComparison.Ordinal) || adUnitId.Length <= 7 || close == null || error == null)
                throw new ArgumentException("Explicit configured unit and observers required.");
            return new Handle(this, adUnitId, close, error);
        }
        bool Reserve(Handle h)
        {
            if (active != null || Mode == PlatformExecutionMode.Native && nativeActive != null) return false;
            active = h; if (Mode == PlatformExecutionMode.Native) nativeActive = h; return true;
        }
        void Released(Handle h) { if (active == h) active = null; if (nativeActive == h) nativeActive = null; }
        sealed class Handle : IWeChatRewardedHandle
        {
            readonly WxRewardedApi owner;
            readonly string unit;
            Action<bool?> close;
            Action<FrameworkError> error;
            readonly Action<WXRewardedVideoAdOnCloseResponse> closeBound;
            readonly Action<WXADErrorResponse> errorBound;
            readonly UniTaskCompletionSource<Result> load = new UniTaskCompletionSource<Result>(), ack = new UniTaskCompletionSource<Result>();
            IWxRewardedObject resource;
            bool loading, loadEnded, loadOk, showing, ackEnded, ackOk, closing, released, canceled, reserved, uncertain;
            bool closeAttached, errorAttached, loadSeen, ackSeen, terminalSeen, terminalDelivered, errorBeforeShowQueued;
            FrameworkError? priorError;
            int dispatchDepth;
            public Handle(WxRewardedApi owner, string unit, Action<bool?> close, Action<FrameworkError> error)
            { this.owner = owner; this.unit = unit; this.close = close; this.error = error; closeBound = Closed; errorBound = Failed; }
            static Result Fail(string code, ErrorKind kind = ErrorKind.InvalidState) => Result.Failure(new FrameworkError(kind, code));
            public UniTask<Result> LoadAsync()
            {
                if (!PlayerLoopHelper.IsMainThread || loading || closing || released || canceled) return UniTask.FromResult(Fail("wechat.ads.load_state"));
                loading = true;
                if (!owner.Reserve(this)) { loadEnded = true; return UniTask.FromResult(Fail("wechat.ads.busy")); }
                reserved = true;
                try { resource = owner.factory(unit); }
                catch (Exception) { uncertain = true; loadEnded = true; return UniTask.FromResult(Fail("wechat.ads.create_uncertain", ErrorKind.Provider)); }
                if (resource == null) { loadEnded = true; return UniTask.FromResult(Fail("wechat.ads.factory_null", ErrorKind.Provider)); }
                try
                {
                    errorAttached = true; resource.OnError(errorBound);
                    closeAttached = true; resource.OnClose(closeBound);
                }
                catch (Exception) { loadEnded = true; return UniTask.FromResult(Fail("wechat.ads.subscribe_failed", ErrorKind.Provider)); }
                try { resource.Load(_ => LoadResult(Result.Success()), r => LoadResult(Result.Failure(Classify(r?.errCode ?? 0)))); }
                catch (Exception) { uncertain = true; } // A throw after dispatch cannot establish actual completion.
                return load.Task;
            }
            void LoadResult(Result result)
            {
                if (released || loadSeen) return; loadSeen = true;
                owner.defer(() => { uncertain = false; loadEnded = true; loadOk = result.IsSuccess; load.TrySetResult(result); });
            }
            public UniTask<Result> ShowAsync()
            {
                if (!PlayerLoopHelper.IsMainThread || !loadEnded || !loadOk || showing || closing || released || canceled)
                    return UniTask.FromResult(Fail("wechat.ads.show_state"));
                if (priorError.HasValue) return UniTask.FromResult(Result.Failure(priorError.Value));
                showing = true;
                try { resource.Show(_ => ShowResult(Result.Success()), r => ShowResult(Result.Failure(priorError ?? Classify(r?.errCode ?? 0)))); }
                catch (Exception) { uncertain = true; }
                return ack.Task;
            }
            void ShowResult(Result result)
            {
                if (released || ackSeen) return; ackSeen = true;
                owner.defer(() => { uncertain = false; ackEnded = true; ackOk = result.IsSuccess; ack.TrySetResult(result); });
            }
            void Closed(WXRewardedVideoAdOnCloseResponse value)
            {
                if (!showing || released || terminalSeen) return;
                terminalSeen = true;
                // SDK bool defaults to false for an absent field; null/missing never prove completed viewing.
                bool ended = value != null && value.isEnded;
                owner.defer(() => { terminalDelivered = true; Dispatch(() => close?.Invoke(ended)); });
            }
            void Failed(WXADErrorResponse value)
            {
                if (released || terminalSeen) return;
                var failure = Classify(value?.errCode ?? 0);
                if (showing) terminalSeen = true;
                else { if (errorBeforeShowQueued) return; errorBeforeShowQueued = true; }
                priorError = failure;
                owner.defer(() => { if (showing && terminalSeen) terminalDelivered = true; Dispatch(() => error?.Invoke(failure)); });
            }
            void Dispatch(Action action) { if (released) return; dispatchDepth++; try { action(); } finally { dispatchDepth--; } }
            public Result CancelLoad()
            {
                if (!PlayerLoopHelper.IsMainThread || showing || released) return Fail("wechat.ads.cancel_state");
                canceled = true; return Result.Success(); // No verified WX abort API; keep actual callbacks and resource owned.
            }
            public UniTask<Result> ReleaseAsync(CleanupContext cleanup)
            {
                if (!PlayerLoopHelper.IsMainThread) return UniTask.FromResult(Fail("wechat.ads.main_thread", ErrorKind.CleanupFailed));
                if (released) return UniTask.FromResult(Result.Success()); closing = true;
                if (uncertain || dispatchDepth != 0 || loading && !loadEnded || showing && (!ackEnded || ackOk && !terminalDelivered))
                    return UniTask.FromResult(Fail("wechat.ads.actual_pending", ErrorKind.CleanupFailed));
                try
                {
                    if (closeAttached) { resource.OffClose(closeBound); closeAttached = false; }
                    if (errorAttached) { resource.OffError(errorBound); errorAttached = false; }
                    resource?.Destroy(); resource = null;
                    released = true; close = null; error = null; if (reserved) owner.Released(this);
                    return UniTask.FromResult(Result.Success());
                }
                catch (Exception) { return UniTask.FromResult(Fail("wechat.ads.release_failed", ErrorKind.CleanupFailed)); }
            }
        }
        sealed class SdkObject : IWxRewardedObject
        {
            readonly WXRewardedVideoAd ad;
            internal SdkObject(WXRewardedVideoAd ad) { this.ad = ad ?? throw new ArgumentNullException(nameof(ad)); }
            public void OnClose(Action<WXRewardedVideoAdOnCloseResponse> f) => ad.OnClose(f);
            public void OffClose(Action<WXRewardedVideoAdOnCloseResponse> f) => ad.OffClose(f);
            public void OnError(Action<WXADErrorResponse> f) => ad.OnError(f);
            public void OffError(Action<WXADErrorResponse> f) => ad.OffError(f);
            public void Load(Action<WXTextResponse> success, Action<WXADErrorResponse> failure) => ad.Load(success, failure);
            public void Show(Action<WXTextResponse> success, Action<WXTextResponse> failure) => ad.Show(success, failure);
            public void Destroy() => ad.Destroy();
        }
    }
}
