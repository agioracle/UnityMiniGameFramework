using System;
using System.Collections.Generic;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat;
namespace MiniFramework.WeChat.Tests
{
    // Raw callback fault injection, always Mock; never certifies a native SDK or identity.
    public sealed class ScriptedWeChatApi : IWeChatApi
    {
        public PlatformExecutionMode Mode => PlatformExecutionMode.Mock;
        public readonly List<Action> Shows = new List<Action>(), Hides = new List<Action>();
        public readonly List<Request> Requests = new List<Request>();
        public bool HoldInitialize, ThrowInitialize, ThrowShowAfterBind, FailOffShow, FailOffHide;
        public bool HoldLogin, AutoReply = true, ThrowStart, ThrowCreate, NullRequest, FailRelease;
        public int InitializeCode, InitializeCalls, CreateCalls, StartCalls, OffShowCalls, OffHideCalls;
        public Action<int> SavedInitialize;
        public void OnShow(Action callback) { Shows.Add(callback); if (ThrowShowAfterBind) throw new InvalidOperationException(); }
        public void OffShow(Action callback) { OffShowCalls++; if (FailOffShow) throw new InvalidOperationException(); Shows.Remove(callback); }
        public void OnHide(Action callback) { Hides.Add(callback); }
        public void OffHide(Action callback) { OffHideCalls++; if (FailOffHide) throw new InvalidOperationException(); Hides.Remove(callback); }
        public void Initialize(Action<int> callback) { InitializeCalls++; SavedInitialize = callback; if (ThrowInitialize) throw new InvalidOperationException(); if (!HoldInitialize) callback(InitializeCode); }
        public void Show() { foreach (var c in Shows.ToArray()) c(); }
        public void Hide() { foreach (var c in Hides.ToArray()) c(); }
        public IWeChatLoginRequest CreateLogin(Action<string> success, Action failure, Action complete)
        {
            CreateCalls++; if (ThrowCreate) throw new InvalidOperationException(); if (NullRequest) return null;
            var request = new Request(this, success, failure, complete); Requests.Add(request); return request;
        }
        public sealed class Request : IWeChatLoginRequest
        {
            readonly ScriptedWeChatApi owner; Action<string> success; Action failure, complete;
            public int ReleaseCalls; public bool ActuallyComplete, Released;
            internal Request(ScriptedWeChatApi owner, Action<string> success, Action failure, Action complete)
            { this.owner = owner; this.success = success; this.failure = failure; this.complete = complete; }
            public void Start() { owner.StartCalls++; if (owner.ThrowStart) throw new InvalidOperationException(); if (owner.AutoReply) Success(); if (!owner.HoldLogin) Complete(); }
            public void Success(string code = "synthetic-code") => success?.Invoke(code);
            public void Fail() => failure?.Invoke();
            public void Complete() { ActuallyComplete = true; complete?.Invoke(); }
            public void Release() { ReleaseCalls++; if (owner.FailRelease) throw new InvalidOperationException(); Released = true; success = null; failure = complete = null; }
        }
    }
}
