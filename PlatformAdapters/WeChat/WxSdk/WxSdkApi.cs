using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using WeChatWASM;

namespace MiniFramework.Providers.WeChat.WxSdk
{
    // Compiled only in the selected, pinned WX profile. Editor SDK simulation is never Native.
    public sealed class WxSdkApi : IWeChatApi, IWeChatMemoryWarningApi
    {
        readonly Dictionary<Action, Action<OnShowListenerResult>> shows = new Dictionary<Action, Action<OnShowListenerResult>>();
        readonly Dictionary<Action, Action<GeneralCallbackResult>> hides = new Dictionary<Action, Action<GeneralCallbackResult>>();
        readonly MemoryWarningBinding memoryWarnings = new MemoryWarningBinding(WX.OnMemoryWarning, WX.OffMemoryWarning);
        public PlatformExecutionMode Mode => PlatformExecutionMode.Native;
        WxSdkApi() { }
        public static Result<IPlatformBackend> CreateNativeBackend()
        {
            var api = CreateNativeApi();
            return api.IsSuccess ? Result<IPlatformBackend>.Success(new WeChatBackend(api.Value))
                : Result<IPlatformBackend>.Failure(api.Error);
        }
        // Cold native binding for explicit provider composition/decorators. The same host
        // gate applies; obtaining this API neither initializes nor dispatches SDK work.
        public static Result<IWeChatApi> CreateNativeApi()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                var info = WX.GetSystemInfoSync();
                if (info != null && IsQualifiedHost(info.platform, info.SDKVersion)) return Result<IWeChatApi>.Success(new WxSdkApi());
            }
            catch (Exception) { }
#endif
            return Result<IWeChatApi>.Failure(new FrameworkError(ErrorKind.NotSupported, "wechat.native_host.required"));
        }
        internal static bool IsQualifiedHost(string platform, string library) =>
            (platform == "ios" || platform == "android" || platform == "devtools" || platform == "windows" || platform == "mac") &&
            Version.TryParse(library, out var version) && version >= new Version(2, 8, 0);
        public void OnShow(Action callback)
        {
            if (shows.ContainsKey(callback)) throw new InvalidOperationException("wechat.show.duplicate_binding");
            Action<OnShowListenerResult> bound = _ => callback(); shows.Add(callback, bound); WX.OnShow(bound);
        }
        public void OffShow(Action callback)
        { if (shows.TryGetValue(callback, out var bound)) { WX.OffShow(bound); shows.Remove(callback); } }
        public void OnHide(Action callback)
        {
            if (hides.ContainsKey(callback)) throw new InvalidOperationException("wechat.hide.duplicate_binding");
            Action<GeneralCallbackResult> bound = _ => callback(); hides.Add(callback, bound); WX.OnHide(bound);
        }
        public void OffHide(Action callback)
        { if (hides.TryGetValue(callback, out var bound)) { WX.OffHide(bound); hides.Remove(callback); } }
        public void OnMemoryWarning(Action callback) => memoryWarnings.On(callback);
        public void OffMemoryWarning(Action callback) => memoryWarnings.Off(callback);

        // Typed SDK delegates stay owned until Off returns; a failed detach remains retryable.
        internal sealed class MemoryWarningBinding
        {
            sealed class Entry { internal Action Callback; internal int Active = 1; internal Action<OnMemoryWarningListenerResult> Bound; }
            readonly Dictionary<Action, Entry> entries = new Dictionary<Action, Entry>();
            readonly Action<Action<OnMemoryWarningListenerResult>> on, off;
            internal int Count => entries.Count;
            internal MemoryWarningBinding(Action<Action<OnMemoryWarningListenerResult>> on, Action<Action<OnMemoryWarningListenerResult>> off)
            { this.on = on ?? throw new ArgumentNullException(nameof(on)); this.off = off ?? throw new ArgumentNullException(nameof(off)); }
            internal void On(Action callback)
            {
                if (callback == null) throw new ArgumentNullException(nameof(callback));
                if (entries.ContainsKey(callback)) throw new InvalidOperationException("wechat.memory.duplicate_binding");
                var entry = new Entry { Callback = callback };
                entry.Bound = _ => { if (Volatile.Read(ref entry.Active) != 0) entry.Callback?.Invoke(); };
                entries.Add(callback, entry); on(entry.Bound);
            }
            internal void Off(Action callback)
            {
                if (!entries.TryGetValue(callback, out var entry)) return;
                off(entry.Bound); Volatile.Write(ref entry.Active, 0); entry.Callback = null; entries.Remove(callback);
            }
        }
        public void Initialize(Action<int> actualComplete) => Initialization.Start(actualComplete);
        // The pinned SDK's WXInitializeSDK sends Inited=200. The portable bridge uses
        // zero for success; never allow native zero to masquerade as SDK success.
        internal static int NormalizeInitializationResult(int sdkCode) => sdkCode == 200 ? 0 : sdkCode == 0 ? -1 : sdkCode;
        public IWeChatLoginRequest CreateLogin(Action<string> success, Action failure, Action actualComplete) =>
            new LoginRequest(success, failure, actualComplete, WX.Login, action => PlayerLoopHelper.AddContinuation(PlayerLoopTiming.LastPostLateUpdate, action));

        // WXSDK owns its global manager. Contexts share a bounded initialization observation;
        // initialization is never replayed by cleanup retry and stores no credentials.
        static class Initialization
        {
            static readonly object Gate = new object();
            static readonly List<Action<int>> Waiters = new List<Action<int>>();
            static bool started; static int? result;
            internal static void Start(Action<int> callback)
            {
                bool dispatch = false; int? cached;
                lock (Gate)
                {
                    cached = result;
                    if (!cached.HasValue)
                    {
                        if (Waiters.Count >= 16) { cached = -1; }
                        else { Waiters.Add(callback); if (!started) { started = true; dispatch = true; } }
                    }
                }
                if (cached.HasValue) { callback(cached.Value); return; }
                if (dispatch) WX.InitSDK(Complete);
            }
            static void Complete(int code)
            {
                code = NormalizeInitializationResult(code);
                Action<int>[] waiters;
                lock (Gate) { if (result.HasValue) return; result = code; waiters = Waiters.ToArray(); Waiters.Clear(); }
                foreach (var callback in waiters) { try { callback(code); } catch (Exception) { } }
            }
        }

        internal sealed class LoginRequest : IWeChatLoginRequest
        {
            readonly LoginOption options;
            readonly Action<LoginOption> dispatch;
            readonly Action<Action> defer;
            Action<string> success; Action failure, terminal;
            int started, resultSeen, terminalQueued, terminalDelivered, released;
            internal LoginRequest(Action<string> success, Action failure, Action terminal, Action<LoginOption> dispatch, Action<Action> defer)
            {
                this.success = success; this.failure = failure; this.terminal = terminal;
                this.dispatch = dispatch; this.defer = defer;
                options = new LoginOption { success = Success, fail = Failure, complete = Complete };
            }
            public void Start()
            { if (Volatile.Read(ref released) != 0 || Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("wechat.login.started"); dispatch(options); }
            void Success(LoginSuccessCallbackResult result)
            { Volatile.Write(ref resultSeen, 1); if (Volatile.Read(ref released) == 0 && Volatile.Read(ref terminalQueued) == 0) success?.Invoke(result?.code); }
            void Failure(LoginFailCallbackErr result)
            { Volatile.Write(ref resultSeen, 1); if (Volatile.Read(ref released) == 0 && Volatile.Read(ref terminalQueued) == 0) failure?.Invoke(); }
            void Complete(GeneralCallbackResult result)
            {
                if (Interlocked.Exchange(ref terminalQueued, 1) != 0) return;
                // SDK complete is the terminal signal. Queueing is a reentrancy barrier so the
                // SDK's callback dispatch can return before consumer cleanup clears its options.
                // It is not a timeout/Yield-based inference that SDK work finished.
                defer(() =>
                { Volatile.Write(ref terminalDelivered, 1); terminal?.Invoke(); });
            }
            public void Release()
            {
                if (Volatile.Read(ref started) != 0 && Volatile.Read(ref terminalDelivered) == 0)
                    throw new InvalidOperationException("wechat.login.actual_pending");
                // SDK OneWayFunction clones these delegates; OneWayCallback removes its entry
                // only after outcome and complete. Complete-only must retain the cleanup lease.
                if (Volatile.Read(ref started) != 0 && Volatile.Read(ref resultSeen) == 0)
                    throw new InvalidOperationException("wechat.login.sdk_result_missing");
                if (Interlocked.Exchange(ref released, 1) != 0) return;
                options.success = null; options.fail = null; options.complete = null;
                success = null; failure = terminal = null;
            }
        }
    }
}
