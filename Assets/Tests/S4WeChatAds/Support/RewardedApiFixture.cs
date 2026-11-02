using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat.Ads;
namespace MiniFramework.Tests.WeChatAds
{
    public sealed class RewardedApiFixture : IWeChatRewardedApi
    {
        public PlatformExecutionMode Mode { get; set; } = PlatformExecutionMode.Mock;
        public int Creates;
        public bool ThrowCreate, NullCreate;
        public readonly List<Handle> Handles = new List<Handle>();
        public Action<Handle> Configure;
        public static IReadOnlyDictionary<string,string> Placements => new Dictionary<string,string>{{"revive","adunit-synthetic-fixture"}};
        public IWeChatRewardedHandle Create(string id, Action<bool?> close, Action<FrameworkError> error)
        {
            Creates++; if (ThrowCreate) throw new InvalidOperationException("private native detail"); if (NullCreate) return null;
            var h = new Handle(id, close, error); Handles.Add(h); Configure?.Invoke(h); return h;
        }
        public sealed class Handle : IWeChatRewardedHandle
        {
            readonly Action<bool?> close; readonly Action<FrameworkError> error;
            readonly UniTaskCompletionSource<Result> load = new UniTaskCompletionSource<Result>(), ack = new UniTaskCompletionSource<Result>(), release = new UniTaskCompletionSource<Result>();
            public readonly string Unit;
            public int Loads, Shows, Cancels, Releases;
            public bool HoldLoad, HoldAck, HoldRelease, FailRelease, ThrowLoad, ThrowShow, ThrowRelease, ThrowCancel;
            public bool? CloseDuringAck;
            public Result LoadResult = Result.Success(), AckResult = Result.Success(), CancelResult = Result.Success();
            public Handle(string id, Action<bool?> close, Action<FrameworkError> error) { Unit=id;this.close=close;this.error=error; }
            public UniTask<Result> LoadAsync() { Loads++; if (ThrowLoad) throw new InvalidOperationException("private"); return HoldLoad?load.Task:UniTask.FromResult(LoadResult); }
            public UniTask<Result> ShowAsync() { Shows++; if (ThrowShow) throw new InvalidOperationException("private"); if(CloseDuringAck.HasValue)close(CloseDuringAck);return HoldAck?ack.Task:UniTask.FromResult(AckResult); }
            public Result CancelLoad() { Cancels++;if(ThrowCancel)throw new InvalidOperationException();return CancelResult; }
            public UniTask<Result> ReleaseAsync(CleanupContext cleanup) { Releases++; if(ThrowRelease)throw new InvalidOperationException("private"); return HoldRelease?release.Task:UniTask.FromResult(FailRelease?Result.Failure(Fault(ErrorKind.CleanupFailed)):Result.Success()); }
            public void CompleteLoad() => load.TrySetResult(LoadResult);
            public void CompleteAck() => ack.TrySetResult(AckResult);
            public void CompleteRelease() => release.TrySetResult(FailRelease?Result.Failure(Fault(ErrorKind.CleanupFailed)):Result.Success());
            public void Close(bool? completed) => close(completed);
            public void Error(ErrorKind kind=ErrorKind.Provider) => error(Fault(kind));
        }
        public static FrameworkError Fault(ErrorKind kind) => new FrameworkError(kind,"native-private-code","private payload");
    }
    public sealed class CallbackSinkFixture : IAdCallbackSink
    {
        public bool Accept=true, Throw;
        public int Posts;
        public readonly List<AdCallback> Facts=new List<AdCallback>();
        public bool TryPost(AdCallback callback) { Posts++;if(Throw)throw new InvalidOperationException();if(!Accept)return false;Facts.Add(callback);return true; }
    }
}
