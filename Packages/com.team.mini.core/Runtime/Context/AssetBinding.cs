using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Runtime
{
    // A single binding owns one visual property for one owner session.
    public sealed class AssetBinding<T>:IResourceBinding,IAsyncCleanup,IDiagnosticSource where T:class
    {
        private readonly IAssetsService assets;private readonly IResourceTarget<T> target;private readonly IScope owner;
        private readonly Scope resources;private readonly CleanupContext reclaim;
        private readonly List<IScope> scopes=new List<IScope>();
        private CancellationTokenSource pending;private IScope current;private T displayed;
        private bool closed,faulted;private UniTask<Result>? closing;private UniTask cleanupDriver;
        public string Id {get;}public long Version {get;private set;}
        private AssetBinding(string id,IAssetsService assets,IResourceTarget<T> target,IScope owner,CleanupContext reclaim)
        {Id=id;this.assets=assets;this.target=target;this.owner=owner;this.reclaim=reclaim;resources=new Scope(id+"/assets");}
        public static Result<AssetBinding<T>> Create(string id,IAssetsService assets,IResourceTarget<T> target,IScope owner,CleanupContext reclaim)
        {
            CleanupWait.MainThread();
            if(string.IsNullOrWhiteSpace(id)||assets==null||target==null||!target.IsAlive||owner==null||owner.State!=ScopeState.Open)
                return Result<AssetBinding<T>>.Failure(new FrameworkError(ErrorKind.InvalidArgument,"binding.arguments"));
            var binding=new AssetBinding<T>(id,assets,target,owner,reclaim);var registered=owner.Cleanup.Register(binding);
            if(!registered.IsSuccess){binding.resources.CloseAsync(reclaim);return Result<AssetBinding<T>>.Failure(registered.Error);}
            return Result<AssetBinding<T>>.Success(binding);
        }
        public async UniTask<Result> SetAsync(ResourceKey key,OperationContext wait)
        {
            CleanupWait.MainThread();if(closed||faulted||owner.State!=ScopeState.Open)return Failure("binding.closed",ErrorKind.InvalidState);
            if(!key.IsValid||key.Kind!=ResourceKind.Asset||key.AssetType!=typeof(T))return Failure("binding.key",ErrorKind.InvalidArgument);
            if(!target.IsAlive){StartClose(reclaim,false);return Failure("binding.target.destroyed",ErrorKind.Canceled);}
            var version=++Version;pending?.Cancel();
            var cancellation=CancellationTokenSource.CreateLinkedTokenSource(wait.Cancellation,owner.LifetimeCancellation);pending=cancellation;
            var child=resources.CreateChild(Id+"/"+version,ScopeKind.View);if(!child.IsSuccess){cancellation.Dispose();pending=null;return Result.Failure(child.Error);}
            var scope=child.Value;scopes.Add(scope);var retained=false;
            try
            {
                var request=assets.Load<T>(key,scope);
                if(!request.IsSuccess)return Result.Failure(request.Error);
                var result=await request.Value.WaitAsync(new OperationContext(cancellation.Token,wait.Timeout));
                if(!result.IsSuccess)return Result.Failure(result.Error);
                if(version!=Version||closed||owner.State!=ScopeState.Open||!target.IsAlive||cancellation.IsCancellationRequested)
                    return Failure("binding.superseded",ErrorKind.Canceled);
                var previous=current;
                // Install ownership before the setter: setters may throw after writing or synchronously close the owner.
                var value=result.Value.Value;current=scope;displayed=value;retained=true;
                try {target.Value=value;}
                catch(Exception e){faulted=true;return Result.Failure(new FrameworkError(ErrorKind.Unexpected,"binding.commit.exception",e.Message));}
                if(previous!=null)previous.CloseAsync(reclaim);
                if(closed||owner.State!=ScopeState.Open||version!=Version)return Failure("binding.commit.interrupted",ErrorKind.Canceled);
                return Result.Success();
            }
            finally
            {
                if(!retained)scope.CloseAsync(reclaim);
                if(ReferenceEquals(pending,cancellation))pending=null;
                cancellation.Dispose();
            }
        }
        public UniTask<Result> CloseAsync(CleanupContext cleanup)=>Observe(StartClose(cleanup,false),cleanup);
        public UniTask<Result> RetryCleanupAsync(CleanupContext cleanup)=>Observe(StartClose(cleanup,true),cleanup);
        public UniTask<Result> CleanupAsync(CleanupContext cleanup)=>StartClose(cleanup,true);
        private UniTask<Result> StartClose(CleanupContext cleanup,bool retry)
        {
            CleanupWait.MainThread();closed=true;Version++;pending?.Cancel();
            if(closing.HasValue&&(closing.Value.Status==UniTaskStatus.Pending||!retry))return closing.Value;
            var completion=new UniTaskCompletionSource<Result>();closing=completion.Task;cleanupDriver=Drain(cleanup,retry,completion).Preserve();return completion.Task;
        }
        private async UniTask Drain(CleanupContext cleanup,bool retry,UniTaskCompletionSource<Result> completion)
        {
            Result result;
            try
            {
                // Don't erase an unrelated value installed externally. Never release a value still shown by this binding.
                if(target.IsAlive&&displayed!=null&&ReferenceEquals(target.Value,displayed))target.Value=null;
                displayed=null;current=null;
                var report=retry?await resources.RetryFailedAsync(cleanup):await resources.CloseAsync(cleanup);
                // First cleanup via parent registry also uses retry=true, so close an open root explicitly.
                if(resources.State==ScopeState.Open)report=await resources.CloseAsync(cleanup);
                while(report.PendingCount>0){await UniTask.Yield();report=resources.Cleanup.Snapshot;}
                result=report.IsFullyDrained?Result.Success():Failure("binding.cleanup.failed",ErrorKind.CleanupFailed);
                if(result.IsSuccess)faulted=false;
            }
            catch(Exception e){result=Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"binding.clear.exception",e.Message));}
            completion.TrySetResult(result);
        }
        public DiagnosticCounters ReadDiagnostics()
        {
            CleanupWait.MainThread();var result=new DiagnosticCounters();foreach(var scope in scopes)
            {if(scope.State!=ScopeState.Closed)result.ActiveScopes++;result.FailedCleanup+=scope.Cleanup.Snapshot.FailedCount;if(scope.State==ScopeState.Closing)result.PendingOperations++;}
            if(faulted)result.FailedCleanup++;return result;
        }
        private static async UniTask<Result> Observe(UniTask<Result> actual,CleanupContext cleanup)
        {var began=CleanupWait.Now;while(true){var interrupted=CleanupWait.Interrupted(CleanupWait.Operation(cleanup),began);if(interrupted.HasValue)return interrupted.Value;if(actual.Status!=UniTaskStatus.Pending)return await actual;await UniTask.Yield();}}
        private static Result Failure(string code,ErrorKind kind)=>Result.Failure(new FrameworkError(kind,code));
    }
}
