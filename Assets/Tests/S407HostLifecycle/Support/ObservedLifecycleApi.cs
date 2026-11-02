using System;
using System.Collections.Generic;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat;

namespace MiniFramework.S407HostLifecycle
{
    // Validation observer. Real callbacks are forwarded synchronously and never fabricated.
    public sealed class ObservedLifecycleApi : IWeChatApi
    {
        readonly IWeChatApi inner;
        readonly Dictionary<Action, Action> shows = new Dictionary<Action, Action>();
        readonly Dictionary<Action, Action> hides = new Dictionary<Action, Action>();
        readonly List<Action> retired = new List<Action>();
        public int ShowOn, ShowOff, HideOn, HideOff, ShowCallbacks, HideCallbacks, ControlledLateCallbacks;
        public readonly List<string> RawOrder = new List<string>();
        public ObservedLifecycleApi(IWeChatApi inner) { this.inner = inner ?? throw new ArgumentNullException(nameof(inner)); }
        public PlatformExecutionMode Mode => inner.Mode;
        public int Listeners => shows.Count + hides.Count;
        void Record(string kind) { if (RawOrder.Count < 64) RawOrder.Add(kind); }
        public void OnShow(Action c) { Action bound = () => { ShowCallbacks++; Record("Shown"); c(); }; shows.Add(c, bound); inner.OnShow(bound); ShowOn++; }
        public void OnHide(Action c) { Action bound = () => { HideCallbacks++; Record("Hidden"); c(); }; hides.Add(c, bound); inner.OnHide(bound); HideOn++; }
        public void OffShow(Action c) { if (!shows.TryGetValue(c, out var bound)) return; inner.OffShow(bound); shows.Remove(c); retired.Add(c); ShowOff++; }
        public void OffHide(Action c) { if (!hides.TryGetValue(c, out var bound)) return; inner.OffHide(bound); hides.Remove(c); retired.Add(c); HideOff++; }
        public void Initialize(Action<int> c) => inner.Initialize(c);
        public IWeChatLoginRequest CreateLogin(Action<string> s, Action f, Action c) => inner.CreateLogin(s, f, c);
        public void InjectRetiredCallbacks()
        {
            // Explicit fault injection, never counted as official SDK callback delivery.
            foreach (var c in retired.ToArray()) { ControlledLateCallbacks++; c(); }
            retired.Clear();
        }
    }
}
