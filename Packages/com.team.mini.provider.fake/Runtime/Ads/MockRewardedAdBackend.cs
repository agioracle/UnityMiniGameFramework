using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Providers.AdsMock
{
    // Fault injection only: no ad SDK, credentials, real placement or rewards.
    public sealed class MockRewardedAdBackend:IRewardedAdBackend
    {
        readonly List<MockRewardedAdAttempt> attempts=new List<MockRewardedAdAttempt>();
        public PlatformDescriptor Descriptor {get;set;}=new PlatformDescriptor("editor-mock",PlatformExecutionMode.Mock,"mock-ads-v1");
        public IReadOnlyList<MockRewardedAdAttempt> Attempts=>attempts.AsReadOnly();
        public bool HoldLoad {get;set;} public bool HoldShow {get;set;} public bool AutoCallbacks {get;set;}=true; public bool Skip {get;set;}
        public bool FailCancel {get;set;} public bool FailCleanup {get;set;} public bool HoldCleanup {get;set;}
        public bool ThrowCreate {get;set;} public bool ThrowLoad {get;set;} public bool ThrowShow {get;set;} public bool ThrowCleanup {get;set;}
        public FrameworkError? LoadError {get;set;} public FrameworkError? ShowError {get;set;}
        public IRewardedAdAttempt Create(string generation,string attemptId,string placementId,IAdCallbackSink sink)
        {
            if(ThrowCreate)throw new InvalidOperationException("Injected factory fault.");
            var attempt=new MockRewardedAdAttempt(this,generation,attemptId,placementId,sink);attempts.Add(attempt);return attempt;
        }
    }
    public sealed class MockRewardedAdAttempt:IRewardedAdAttempt
    {
        readonly MockRewardedAdBackend owner;readonly object gate=new object();IAdCallbackSink sink;long sequence;bool closed,loadDone,showDone;
        readonly UniTaskCompletionSource<Result> loaded=new UniTaskCompletionSource<Result>(),shown=new UniTaskCompletionSource<Result>(),cleaned=new UniTaskCompletionSource<Result>();
        public string Generation {get;}public string AttemptId {get;}public string PlacementId {get;}
        public int LoadCount {get;private set;}public int ShowCount {get;private set;}public int CancelCount {get;private set;}public int CleanupCount {get;private set;}
        internal MockRewardedAdAttempt(MockRewardedAdBackend owner,string generation,string attempt,string placement,IAdCallbackSink sink)
        {this.owner=owner;Generation=generation;AttemptId=attempt;PlacementId=placement;this.sink=sink??throw new ArgumentNullException(nameof(sink));}
        static FrameworkError Error(string code,ErrorKind kind=ErrorKind.Provider)=>new FrameworkError(kind,code);
        public UniTask<Result> LoadAsync()
        {
            if(closed||LoadCount!=0)return UniTask.FromResult(Result.Failure(Error("ads.mock.load_state",ErrorKind.InvalidState)));
            LoadCount++;if(owner.ThrowLoad){loadDone=true;throw new InvalidOperationException();}
            if(!owner.HoldLoad)CompleteLoad();return loaded.Task;
        }
        public bool CompleteLoad(Result? result=null)
        {loadDone=true;return loaded.TrySetResult(result??(owner.LoadError.HasValue?Result.Failure(owner.LoadError.Value):Result.Success()));}
        public UniTask<Result> ShowAsync()
        {
            if(closed||!loadDone||ShowCount!=0)return UniTask.FromResult(Result.Failure(Error("ads.mock.show_state",ErrorKind.InvalidState)));
            ShowCount++;if(owner.ThrowShow){showDone=true;throw new InvalidOperationException();}
            if(owner.AutoCallbacks){Publish(AdCallbackKind.Started);if(!owner.Skip)Publish(AdCallbackKind.Completed);Publish(AdCallbackKind.Closed);}
            if(!owner.HoldShow)CompleteShow();return shown.Task;
        }
        public bool CompleteShow(Result? result=null)
        {showDone=true;return shown.TrySetResult(result??(owner.ShowError.HasValue?Result.Failure(owner.ShowError.Value):Result.Success()));}
        public bool Publish(AdCallbackKind kind,long? sequence=null,string generation=null,string attemptId=null,FrameworkError? error=null)
        {
            var callback=new AdCallback(generation??Generation,attemptId??AttemptId,sequence??Interlocked.Increment(ref this.sequence),kind,error);
            lock(gate)return !closed&&sink!=null&&sink.TryPost(callback);
        }
        public Result CancelLoad(){CancelCount++;return owner.FailCancel?Result.Failure(Error("ads.mock.cancel")):Result.Success();}
        public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            CleanupCount++;if(owner.ThrowCleanup)throw new InvalidOperationException();
            if(owner.FailCleanup||LoadCount>0&&!loadDone||ShowCount>0&&!showDone)return Result.Failure(Error("ads.mock.cleanup",ErrorKind.CleanupFailed));
            if(owner.HoldCleanup){var r=await cleaned.Task;if(!r.IsSuccess)return r;}
            lock(gate){closed=true;sink=null;}return Result.Success();
        }
        public bool CompleteCleanup(Result? result=null)=>cleaned.TrySetResult(result??Result.Success());
    }
}
