using System;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat;
namespace MiniFramework.S407RealPool
{
    // Transparent official C# SDK observer in the SDK profile; controlled source in Editor tests.
    public sealed class ObservedMemoryApi : IWeChatApi, IWeChatMemoryWarningApi
    {
        readonly IWeChatApi api;
        readonly IWeChatMemoryWarningApi memory;
        Action callback, bound, saved;
        public int OnCount, OffCount, NativeCallbacks, ControlledSignals, LateSignals;
        public ObservedMemoryApi(IWeChatApi api)
        { this.api=api; memory=(IWeChatMemoryWarningApi)api; }
        public PlatformExecutionMode Mode => api.Mode;
        public void Initialize(Action<int> c) => api.Initialize(c);
        public void OnShow(Action c) => api.OnShow(c);
        public void OffShow(Action c) => api.OffShow(c);
        public void OnHide(Action c) => api.OnHide(c);
        public void OffHide(Action c) => api.OffHide(c);
        public IWeChatLoginRequest CreateLogin(Action<string> s, Action f, Action c) => api.CreateLogin(s,f,c);
        public void OnMemoryWarning(Action c)
        {
            callback=saved=c; bound=()=>{NativeCallbacks++;c();};
            memory.OnMemoryWarning(bound); OnCount++;
        }
        public void OffMemoryWarning(Action c)
        { memory.OffMemoryWarning(bound); OffCount++; callback=null; }
        public void Emit() { ControlledSignals++; callback(); }
        public void EmitLate() { LateSignals++; saved(); }
    }
}
