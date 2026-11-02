using System;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;

namespace MiniFramework.Providers.WeChat
{
    // The concrete WX binding lives outside imported Assets until a WeChat profile is selected.
    // Callbacks may arrive on any thread. Complete must follow SDK complete dispatch, not success.
    public interface IWeChatApi
    {
        PlatformExecutionMode Mode { get; }
        void OnShow(Action callback);
        void OffShow(Action callback);
        void OnHide(Action callback);
        void OffHide(Action callback);
        void Initialize(Action<int> actualComplete);
        IWeChatLoginRequest CreateLogin(Action<string> success, Action failure, Action actualComplete);
    }

    public interface IWeChatLoginRequest
    {
        void Start();
        void Release(); // Clear SDK option callbacks; only after actual complete/dispatch return.
    }

    // Optional extension: existing adapters retain their show/hide contract unchanged.
    public interface IWeChatMemoryWarningApi
    {
        void OnMemoryWarning(Action callback);
        void OffMemoryWarning(Action callback);
    }

    public sealed class WeChatBackend : IPlatformBackend
    {
        readonly IWeChatApi api;
        public PlatformDescriptor Descriptor { get; }
        public WeChatBackend(IWeChatApi api)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            Descriptor = new PlatformDescriptor("wechat", api.Mode, "wx-202610200817-v1");
        }
        public IPlatformSession Open(string generation, IPlatformCallbackSink sink)
        {
            if (string.IsNullOrWhiteSpace(generation) || generation.Length > 64) throw new ArgumentException(nameof(generation));
            return new Session(api, Descriptor, generation, sink ?? throw new ArgumentNullException(nameof(sink)));
        }
        static FrameworkError Error(string code, ErrorKind kind = ErrorKind.Provider) => new FrameworkError(kind, code);
        static PlatformCapabilities Capabilities(bool memoryWarnings) => new PlatformCapabilities(new[] {
            new PlatformCapability(PlatformFeature.Lifecycle, CapabilitySupport.Supported, CapabilityAvailability.Available, "wechat.lifecycle.ready"),
            new PlatformCapability(PlatformFeature.MemoryWarning, memoryWarnings ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,
                memoryWarnings ? CapabilityAvailability.Available : CapabilityAvailability.Unavailable,
                memoryWarnings ? "wechat.memory.ready" : "wechat.memory.unsupported"),
            // wx.login captures a code, without requesting profile permission or claiming a business account.
            new PlatformCapability(PlatformFeature.LoginCredential, CapabilitySupport.Supported, CapabilityAvailability.Available, "wechat.login.ready", CapabilityConstraints.None, 1)
        });

        sealed class Session : IPlatformSession
        {
            readonly object gate = new object();
            readonly IWeChatApi api; readonly PlatformDescriptor descriptor; readonly string generation;
            readonly UniTaskCompletionSource<Result<PlatformStartup>> ready = new UniTaskCompletionSource<Result<PlatformStartup>>();
            readonly Action show, hide, memoryWarning;
            readonly IWeChatMemoryWarningApi memoryApi;
            IPlatformCallbackSink sink; Attempt attempt;
            bool initializing, initEnded, initSucceeded, shownBound, hiddenBound, memoryBound, closing, closed;
            long sequence; PlatformVisibility visibility = PlatformVisibility.Unknown;
            internal Session(IWeChatApi api, PlatformDescriptor descriptor, string generation, IPlatformCallbackSink sink)
            { this.api = api; this.descriptor = descriptor; this.generation = generation; this.sink = sink; show = () => Lifecycle(true); hide = () => Lifecycle(false); memoryApi = api as IWeChatMemoryWarningApi; memoryWarning = MemoryWarning; }
            public UniTask<Result<PlatformStartup>> InitializeAsync()
            {
                lock (gate)
                {
                    if (initializing || closing) return UniTask.FromResult(Result<PlatformStartup>.Failure(Error("wechat.initialize.state", ErrorKind.InvalidState)));
                    initializing = true;
                }
                try
                {
                    // Mark before registration: even a throw after side effects must be rolled back.
                    shownBound = true; api.OnShow(show); hiddenBound = true; api.OnHide(hide);
                    if (memoryApi != null) { memoryBound = true; memoryApi.OnMemoryWarning(memoryWarning); }
                }
                catch (Exception)
                {
                    lock (gate) initEnded = true;
                    ready.TrySetResult(Result<PlatformStartup>.Failure(Error("wechat.subscribe.failed"))); return ready.Task;
                }
                try { api.Initialize(Initialized); }
                catch (Exception)
                {
                    // A throw cannot prove that SDK work never began. Retain the session until its
                    // actual callback; caller cancellation/timeout remains an observation contract.
                }
                return ready.Task;
            }
            void Initialized(int code)
            {
                Result<PlatformStartup> result;
                lock (gate)
                {
                    if (initEnded) return;
                    initEnded = true; initSucceeded = code == 0;
                    result = initSucceeded ? Result<PlatformStartup>.Success(new PlatformStartup(Capabilities(memoryApi != null), visibility, sequence))
                        : Result<PlatformStartup>.Failure(Error("wechat.initialize.failed"));
                }
                ready.TrySetResult(result);
            }
            void Lifecycle(bool foreground)
            {
                lock (gate)
                {
                    if (closing || closed || sequence == long.MaxValue) return;
                    visibility = foreground ? PlatformVisibility.Foreground : PlatformVisibility.Background;
                    Post(PlatformCallback.LifecycleChanged(generation, ++sequence, foreground ? PlatformLifecycleKind.Shown : PlatformLifecycleKind.Hidden));
                }
            }
            void MemoryWarning()
            {
                lock (gate)
                {
                    // A pressure signal is not a foreground transition or a completion fact.
                    if (!initSucceeded || closing || closed || sequence == long.MaxValue) return;
                    Post(PlatformCallback.LifecycleChanged(generation, ++sequence, PlatformLifecycleKind.MemoryWarning));
                }
            }
            internal void Post(PlatformCallback callback)
            {
                lock (gate)
                {
                    try { if (!closing && sink != null && sink.TryPost(callback)) return; }
                    catch (Exception) { }
                }
                callback.Discard();
            }
            public IPlatformLoginAttempt CreateLogin(string attemptId, PlatformLoginOptions options)
            {
                lock (gate)
                {
                    if (closing || !initSucceeded || attempt != null) throw new InvalidOperationException("wechat.login.state");
                    if (string.IsNullOrWhiteSpace(attemptId) || attemptId.Length > 64) throw new ArgumentException(nameof(attemptId));
                    attempt = new Attempt(this, api, descriptor, generation, attemptId); return attempt;
                }
            }
            internal void Released(Attempt value) { lock (gate) if (attempt == value) attempt = null; }
            public UniTask<Result> CleanupAsync(CleanupContext cleanup)
            {
                lock (gate)
                {
                    if (closed) return UniTask.FromResult(Result.Success());
                    closing = true;
                    if (initializing && !initEnded || attempt != null)
                        return UniTask.FromResult(Result.Failure(Error("wechat.session.actual_pending", ErrorKind.CleanupFailed)));
                }
                bool failed = false;
                if (shownBound) { try { api.OffShow(show); shownBound = false; } catch (Exception) { failed = true; } }
                if (hiddenBound) { try { api.OffHide(hide); hiddenBound = false; } catch (Exception) { failed = true; } }
                if (memoryBound) { try { memoryApi.OffMemoryWarning(memoryWarning); memoryBound = false; } catch (Exception) { failed = true; } }
                if (failed) return UniTask.FromResult(Result.Failure(Error("wechat.session.detach_failed", ErrorKind.CleanupFailed)));
                lock (gate) { closed = true; sink = null; }
                return UniTask.FromResult(Result.Success());
            }
        }

        sealed class Attempt : IPlatformLoginAttempt
        {
            readonly object gate = new object(); readonly Session session; readonly IWeChatApi api;
            readonly PlatformDescriptor descriptor; readonly string generation, id;
            readonly UniTaskCompletionSource<Result> ended = new UniTaskCompletionSource<Result>();
            IWeChatLoginRequest request;
            bool started, dispatchReturned, complete, resultSeen, released;
            internal Attempt(Session session, IWeChatApi api, PlatformDescriptor descriptor, string generation, string id)
            { this.session = session; this.api = api; this.descriptor = descriptor; this.generation = generation; this.id = id; }
            public UniTask<Result> StartAsync()
            {
                lock (gate)
                {
                    if (started || released) return UniTask.FromResult(Result.Failure(Error("wechat.login.single_use", ErrorKind.InvalidState)));
                    started = true;
                }
                try { request = api.CreateLogin(Success, Failure, Complete); }
                catch (Exception)
                { // Cold request construction has no SDK effects by contract.
                    lock (gate) { complete = dispatchReturned = true; }
                    ended.TrySetResult(Result.Failure(Error("wechat.login.create_failed"))); return ended.Task;
                }
                if (request == null)
                { lock (gate) { complete = dispatchReturned = true; } ended.TrySetResult(Result.Failure(Error("wechat.login.null_request"))); return ended.Task; }
                try { request.Start(); }
                catch (Exception) { PublishFailure("wechat.login.dispatch_unknown"); }
                finally { lock (gate) { dispatchReturned = true; if (complete) ended.TrySetResult(Result.Success()); } }
                return ended.Task;
            }
            void Success(string code)
            {
                lock (gate)
                {
                    if (resultSeen || complete || released) return; resultSeen = true;
                    if (string.IsNullOrEmpty(code) || code.Length > 16384)
                    { session.Post(PlatformCallback.Login(generation, id, Result<PlatformLoginCredential>.Failure(Error("wechat.login.invalid_code", ErrorKind.Integrity)))); return; }
                    var credential = new PlatformLoginCredential(descriptor.Id, id, descriptor.Mode, code);
                    session.Post(PlatformCallback.Login(generation, id, Result<PlatformLoginCredential>.Success(credential)));
                }
            }
            void Failure() => PublishFailure("wechat.login.failed");
            void PublishFailure(string code)
            {
                lock (gate)
                {
                    if (resultSeen || complete || released) return; resultSeen = true;
                    // The pinned SDK's login failure has errno/errMsg, but errno meanings are not
                    // yet certified. Preserve a stable Provider failure; never guess from text.
                    session.Post(PlatformCallback.Login(generation, id, Result<PlatformLoginCredential>.Failure(Error(code))));
                }
            }
            void Complete()
            {
                lock (gate) { if (complete) return; complete = true; if (dispatchReturned) ended.TrySetResult(Result.Success()); }
            }
            public Result Cancel() => Result.Success(); // This SDK has no verified login abort; intent only.
            public UniTask<Result> CleanupAsync(CleanupContext cleanup)
            {
                lock (gate)
                {
                    if (released) return UniTask.FromResult(Result.Success());
                    if (started && (!complete || !dispatchReturned)) return UniTask.FromResult(Result.Failure(Error("wechat.login.actual_pending", ErrorKind.CleanupFailed)));
                }
                try { request?.Release(); }
                catch (Exception) { return UniTask.FromResult(Result.Failure(Error("wechat.login.release_failed", ErrorKind.CleanupFailed))); }
                lock (gate) { released = true; request = null; }
                session.Released(this); return UniTask.FromResult(Result.Success());
            }
        }
    }
}
