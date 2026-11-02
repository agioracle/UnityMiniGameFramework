using System;
using System.Collections.Generic;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat;
using MiniFramework.WeChat.Tests;
namespace MiniFramework.S407Memory.Tests
{
    // Controlled callbacks only; this does not simulate OS memory allocation or claim Native.
    public sealed class MemoryApi : IWeChatApi, IWeChatMemoryWarningApi
    {
        public readonly ScriptedWeChatApi Inner = new ScriptedWeChatApi();
        public readonly List<Action> Memory = new List<Action>();
        public bool FailOnAfter, FailOff, FailOffAfter;
        public int OnCalls, OffCalls;
        public PlatformExecutionMode Mode => PlatformExecutionMode.Mock;
        public void OnShow(Action callback) => Inner.OnShow(callback);
        public void OffShow(Action callback) => Inner.OffShow(callback);
        public void OnHide(Action callback) => Inner.OnHide(callback);
        public void OffHide(Action callback) => Inner.OffHide(callback);
        public void Initialize(Action<int> callback) => Inner.Initialize(callback);
        public IWeChatLoginRequest CreateLogin(Action<string> success, Action failure, Action complete) => Inner.CreateLogin(success, failure, complete);
        public void OnMemoryWarning(Action callback) { OnCalls++; Memory.Add(callback); if (FailOnAfter) throw new InvalidOperationException(); }
        public void OffMemoryWarning(Action callback) { OffCalls++; if (FailOff) throw new InvalidOperationException(); Memory.Remove(callback); if (FailOffAfter) throw new InvalidOperationException(); }
        public void Raise() { foreach (var callback in Memory.ToArray()) callback(); }
    }
}
