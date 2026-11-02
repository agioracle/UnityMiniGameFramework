using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;

namespace MiniFramework.Providers.WeChat.Ads
{
    // Create is managed-only and cold. The handle owns every partial native allocation/listener.
    // The native binding is materialized only in a selected SDK profile, never in the main project.
    public interface IWeChatRewardedApi
    {
        PlatformExecutionMode Mode { get; }
        IWeChatRewardedHandle Create(string adUnitId, Action<bool?> close, Action<FrameworkError> error);
    }
    public interface IWeChatRewardedHandle
    {
        UniTask<Result> LoadAsync(); // Actual command completion, after native callback dispatch.
        UniTask<Result> ShowAsync(); // Command acknowledgement only; NOT the presentation terminal.
        Result CancelLoad(); // Request only. It must not fabricate Load completion or close a visible ad.
        UniTask<Result> ReleaseAsync(CleanupContext cleanup); // Actual Off/Destroy completion, retryable on failure.
    }

    public sealed class WeChatRewardedBackend : IRewardedAdBackend
    {
        readonly IWeChatRewardedApi api;
        readonly Dictionary<string, string> placements = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly object gate = new object();
        readonly int ownerThread, limit;
        Attempt active;
        int retained;
        public PlatformDescriptor Descriptor { get; }
        public int RetainedAttempts { get { lock (gate) return retained; } }
        public int ActiveAttemptCount { get { lock (gate) return active == null ? 0 : 1; } }
        public WeChatRewardedBackend(IWeChatRewardedApi api, IReadOnlyDictionary<string, string> placements, int maxAttempts = 16)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            if (placements == null || placements.Count < 1 || placements.Count > 32 || maxAttempts < 1 || maxAttempts > 1024)
                throw new ArgumentException("Bounded explicit ad configuration required.");
            foreach (var p in placements)
            {
                if (!PlatformIdentifier.IsValid(p.Key) || !ClaimOwnerSnapshot.IsToken(p.Value) || !p.Value.StartsWith("adunit-", StringComparison.Ordinal) || p.Value.Length <= 7)
                    throw new ArgumentException("Invalid placement configuration.");
                this.placements.Add(p.Key, p.Value);
            }
            Descriptor = new PlatformDescriptor("wechat", api.Mode, "wx-rewarded-bridge-v1");
            ownerThread = Thread.CurrentThread.ManagedThreadId; limit = maxAttempts;
        }
        bool Main => Thread.CurrentThread.ManagedThreadId == ownerThread;
        static FrameworkError Error(string code, ErrorKind kind = ErrorKind.Provider) => new FrameworkError(kind, code);
        static Result Fail(string code, ErrorKind kind = ErrorKind.InvalidState) => Result.Failure(Error(code, kind));
        static FrameworkError Clean(FrameworkError e)
        {
            switch (e.Kind)
            {
                case ErrorKind.NoFill: return Error("wechat.ads.no_fill", e.Kind);
                case ErrorKind.PermissionDenied: return Error("wechat.ads.permission", e.Kind);
                case ErrorKind.Network: return Error("wechat.ads.network", e.Kind);
                case ErrorKind.Unavailable: return Error("wechat.ads.unavailable", e.Kind);
                case ErrorKind.NotSupported: return Error("wechat.ads.unsupported", e.Kind);
                case ErrorKind.InvalidArgument: return Error("wechat.ads.configuration", e.Kind);
                case ErrorKind.Canceled: return Error("wechat.ads.canceled", e.Kind);
                case ErrorKind.Timeout: return Error("wechat.ads.native_timeout", e.Kind);
                default: return Error("wechat.ads.native");
            }
        }
        public IRewardedAdAttempt Create(string generation, string attemptId, string placementId, IAdCallbackSink sink)
        {
            if (!Main) throw new InvalidOperationException("Owner thread required.");
            if (!ClaimOwnerSnapshot.IsToken(generation) || !ClaimOwnerSnapshot.IsToken(attemptId) || sink == null || placementId == null || !placements.TryGetValue(placementId, out var unit))
                throw new ArgumentException("Explicit attempt and configured placement required.");
            lock (gate)
            {
                if (retained >= limit) throw new InvalidOperationException("Attempt capacity exceeded.");
                retained++; return new Attempt(this, generation, attemptId, unit, sink);
            }
        }
        bool Reserve(Attempt value) { lock (gate) { if (active != null) return false; active = value; return true; } }
        void Release(Attempt value) { lock (gate) { if (active == value) active = null; retained--; } }

        sealed class Attempt : IRewardedAdAttempt
        {
            readonly WeChatRewardedBackend owner;
            readonly string generation, attempt, unit;
            readonly IAdCallbackSink sink;
            readonly object gate = new object();
            readonly UniTaskCompletionSource<Result> loaded = new UniTaskCompletionSource<Result>();
            readonly UniTaskCompletionSource<Result> shown = new UniTaskCompletionSource<Result>();
            IWeChatRewardedHandle handle;
            UniTask loadDriver, showDriver, releaseDriver;
            UniTaskCompletionSource<Result> releaseFlight;
            bool loadStarted, loadEnded, loadSucceeded, canceled, showStarted, showEnded, terminal, closing, released;
            bool? ended;
            FrameworkError? loadError, terminalError;
            int callbackDepth;
            internal Attempt(WeChatRewardedBackend owner, string generation, string attempt, string unit, IAdCallbackSink sink)
            { this.owner = owner; this.generation = generation; this.attempt = attempt; this.unit = unit; this.sink = sink; }
            public UniTask<Result> LoadAsync()
            {
                if (!owner.Main) return UniTask.FromResult(Fail("wechat.ads.thread"));
                lock (gate)
                {
                    if (released || closing || canceled || loadStarted) return UniTask.FromResult(Fail("wechat.ads.load_state"));
                    loadStarted = true;
                    if (!owner.Reserve(this)) { loadEnded = true; return UniTask.FromResult(Fail("wechat.ads.busy")); }
                    loadDriver = Load().Preserve(); return loaded.Task;
                }
            }
            async UniTask Load()
            {
                Result r;
                try
                {
                    handle = owner.api.Create(unit, Closed, Failed);
                    r = handle == null ? Fail("wechat.ads.factory_null", ErrorKind.Provider) : await handle.LoadAsync();
                }
                catch (Exception) { r = Fail("wechat.ads.load_exception", ErrorKind.Provider); }
                lock (gate)
                {
                    if (!r.IsSuccess) r = Result.Failure(Clean(r.Error));
                    else if (loadError.HasValue) r = Result.Failure(loadError.Value);
                    loadEnded = true; loadSucceeded = r.IsSuccess;
                }
                loaded.TrySetResult(r);
            }
            public UniTask<Result> ShowAsync()
            {
                if (!owner.Main) return UniTask.FromResult(Fail("wechat.ads.thread"));
                lock (gate)
                {
                    if (released || closing || canceled || !loadEnded || !loadSucceeded || showStarted || handle == null)
                        return UniTask.FromResult(Fail("wechat.ads.show_state"));
                    if (loadError.HasValue) return UniTask.FromResult(Result.Failure(loadError.Value));
                    showStarted = true; showDriver = Show().Preserve(); return shown.Task;
                }
            }
            async UniTask Show()
            {
                Result ack;
                try { ack = await handle.ShowAsync(); }
                catch (Exception) { ack = Fail("wechat.ads.show_exception", ErrorKind.Provider); }
                if (!ack.IsSuccess)
                {
                    var error = Clean(ack.Error);
                    await Post(new AdCallback(generation, attempt, 1, AdCallbackKind.Failed, error));
                    lock (gate) showEnded = true;
                    shown.TrySetResult(Result.Failure(error)); return;
                }
                // Started means the native Show command was acknowledged, not proof of a watched frame.
                await Post(new AdCallback(generation, attempt, 1, AdCallbackKind.Started));
                FrameworkError? failure; bool? completed;
                while (true)
                {
                    lock (gate) { if (terminal) { failure = terminalError; completed = ended; break; } }
                    await UniTask.Yield();
                }
                if (failure.HasValue) await Post(new AdCallback(generation, attempt, 2, AdCallbackKind.Failed, failure.Value));
                else
                {
                    long sequence = 2;
                    if (completed == true) await Post(new AdCallback(generation, attempt, sequence++, AdCallbackKind.Completed));
                    await Post(new AdCallback(generation, attempt, sequence, AdCallbackKind.Closed));
                }
                lock (gate) showEnded = true;
                shown.TrySetResult(failure.HasValue ? Result.Failure(failure.Value) : Result.Success());
            }
            async UniTask Post(AdCallback fact)
            {
                // At most one in-flight fact per driver; rejected ownership stays here. Never retry Load/Show.
                while (true)
                {
                    bool accepted; try { accepted = sink.TryPost(fact); } catch (Exception) { accepted = false; }
                    if (accepted) return;
                    await UniTask.Yield();
                }
            }
            void Closed(bool? complete)
            {
                lock (gate)
                {
                    callbackDepth++;
                    try { if (showStarted && !terminal && !showEnded && !released) { terminal = true; ended = complete; } }
                    finally { callbackDepth--; }
                }
            }
            void Failed(FrameworkError error)
            {
                lock (gate)
                {
                    callbackDepth++;
                    try
                    {
                        if (released || showEnded) return;
                        if (!showStarted) { if (loadStarted && !loadError.HasValue) loadError = Clean(error); }
                        else if (!terminal) { terminal = true; terminalError = Clean(error); }
                    }
                    finally { callbackDepth--; }
                }
            }
            public Result CancelLoad()
            {
                if (!owner.Main) return Fail("wechat.ads.thread");
                lock (gate)
                {
                    if (released || showStarted) return Fail("wechat.ads.cancel_state");
                    if (canceled) return Result.Success();
                    canceled = true;
                    if (!loadStarted || loadEnded || handle == null) return Result.Success();
                    try { var r = handle.CancelLoad(); return r.IsSuccess ? r : Result.Failure(Clean(r.Error)); }
                    catch (Exception) { return Fail("wechat.ads.cancel_exception", ErrorKind.Provider); }
                }
            }
            public UniTask<Result> CleanupAsync(CleanupContext cleanup)
            {
                if (!owner.Main) return UniTask.FromResult(Fail("wechat.ads.thread", ErrorKind.CleanupFailed));
                lock (gate)
                {
                    if (released) return UniTask.FromResult(Result.Success());
                    closing = true;
                    if (callbackDepth > 0 || loadStarted && !loadEnded || showStarted && !showEnded)
                        return UniTask.FromResult(Fail("wechat.ads.actual_pending", ErrorKind.CleanupFailed));
                    if (releaseFlight != null && releaseFlight.Task.Status == UniTaskStatus.Pending) return releaseFlight.Task;
                    releaseFlight = new UniTaskCompletionSource<Result>(); releaseDriver = Release(cleanup).Preserve(); return releaseFlight.Task;
                }
            }
            async UniTask Release(CleanupContext cleanup)
            {
                Result result;
                try { result = handle == null ? Result.Success() : await handle.ReleaseAsync(cleanup); }
                catch (Exception) { result = Fail("wechat.ads.release_exception", ErrorKind.CleanupFailed); }
                lock (gate)
                {
                    if (result.IsSuccess) { released = true; handle = null; owner.Release(this); }
                    else result = Fail("wechat.ads.release_failed", ErrorKind.CleanupFailed);
                }
                releaseFlight.TrySetResult(result);
            }
        }
    }
}
