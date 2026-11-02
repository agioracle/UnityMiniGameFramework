using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using YooAsset;
using Object = UnityEngine.Object;

namespace MiniFramework.Providers.YooAsset
{
    // This is an SDK adapter, not a second resource cache. The caller owns package initialization.
    public sealed class YooAssetProvider : IAssetProvider, IAssetDownloadProvider, ISceneProvider, IModule, IAsyncCleanup
    {
        private readonly IYooPackage backend;
        private readonly string version;
        private readonly HashSet<IYooHandle> handles = new HashSet<IYooHandle>();
        private readonly HashSet<IYooHandle> failedLoads = new HashSet<IYooHandle>();
        private bool stopped, registered, cleanupAttempted;
        private int loading,downloading;
        public int DownloadStartCount {get;private set;}
        public int ActualDownloadCount=>downloading;
        public string Id { get; }
        public IReadOnlyList<string> Dependencies => Array.Empty<string>();
        public int LoadStartCount { get; private set; }
        public int RetainedHandleCount => handles.Count;
        public int FailedLoadCleanupCount => failedLoads.Count;
        public bool SupportsDeferredActivation => false;

        public YooAssetProvider(ResourcePackage package, string contentVersion, string id = "yooasset")
            : this(new YooPackage(package), contentVersion, id) { }
        internal YooAssetProvider(IYooPackage backend, string contentVersion, string id = "yooasset")
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            if (string.IsNullOrWhiteSpace(contentVersion) || string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Frozen version and provider id required.");
            version = contentVersion; Id = id;
        }
        public UniTask<Result> InitializeAsync(IModuleContext context, OperationContext operation)
        {
            if (!PlayerLoopHelper.IsMainThread || registered || stopped)
                return UniTask.FromResult(Fail("yoo.initialize.state", ErrorKind.InvalidState));
            if (operation.Cancellation.IsCancellationRequested)
                return UniTask.FromResult(Fail("yoo.initialize.canceled", ErrorKind.Canceled));
            if (operation.Timeout == TimeSpan.Zero)
                return UniTask.FromResult(Fail("yoo.initialize.timeout", ErrorKind.Timeout));
            var ticket = context.Cleanup.Register(this);
            registered = ticket.IsSuccess;
            return UniTask.FromResult(ticket.IsSuccess ? Result.Success() : Result.Failure(ticket.Error));
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup) { stopped = true; return UniTask.FromResult(Result.Success()); }
        public UniTask<Result> DisposeAsync(CleanupContext cleanup) => UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            if (!PlayerLoopHelper.IsMainThread) return Fail("yoo.thread", ErrorKind.InvalidState);
            stopped = true;
            var retry = cleanupAttempted; cleanupAttempted = true;
            // This is actual drain; a Scope/Context may bound its own observation separately.
            while (loading != 0 || downloading != 0) await UniTask.Yield();
            if (retry)
                foreach (var handle in new List<IYooHandle>(failedLoads)) TryRelease(handle);
            return handles.Count == 0 ? Result.Success() : Fail("yoo.cleanup.retained", ErrorKind.CleanupFailed);
        }
        public Result<IProviderOperation<IProviderAsset<T>>> BeginLoad<T>(ResourceKey key) where T : class => Load<T>(key,false);
        public Result<IProviderOperation<IProviderAsset<T>>> BeginLoadPrepared<T>(ResourceKey key) where T : class => Load<T>(key,true);
        public Result<IProviderOperation<Unit>> BeginDownload(ResourceKey key)
        {
            if(!PlayerLoopHelper.IsMainThread)return Reject<IProviderOperation<Unit>>("yoo.thread",ErrorKind.InvalidState);
            if(stopped)return Reject<IProviderOperation<Unit>>("yoo.stopped",ErrorKind.NotReady);
            if(!key.IsValid||key.Kind!=ResourceKind.Asset||key.Provider!=Id||key.Package!=backend.Name||key.ContentVersion!=version)
                return Reject<IProviderOperation<Unit>>("yoo.key",ErrorKind.InvalidArgument);
            if(!typeof(Object).IsAssignableFrom(key.AssetType))return Reject<IProviderOperation<Unit>>("yoo.type",ErrorKind.NotSupported);
            if(!(backend is IYooDownloadPackage downloads))return Reject<IProviderOperation<Unit>>("yoo.download.unsupported",ErrorKind.NotSupported);
            try
            {
                var ready=backend.ValidateVersion(version);if(!ready.IsSuccess)return Result<IProviderOperation<Unit>>.Failure(ready.Error);
                var started=downloads.Download(key.Address,key.AssetType);
                if(!started.IsSuccess)return started;
                if(started.Value==null)return Reject<IProviderOperation<Unit>>("yoo.download.null",ErrorKind.Integrity);
                downloading++;DownloadStartCount++;var operation=new ProviderOperation<Unit>();operation.Retain(CompleteDownload(started.Value,operation));
                return Result<IProviderOperation<Unit>>.Success(operation);
            }
            catch(Exception e){return Reject<IProviderOperation<Unit>>("yoo.download.start",ErrorKind.Provider,e.Message);}
        }
        private async UniTask CompleteDownload(IProviderOperation<Unit> actual,ProviderOperation<Unit> operation)
        {
            Result<Unit> result;
            try{result=await actual.Completion;}
            catch(Exception e){result=Reject<Unit>("yoo.download.exception",ErrorKind.Provider,e.Message);}
            await UniTask.SwitchToMainThread();downloading--;operation.Complete(result);
        }
        private Result<IProviderOperation<IProviderAsset<T>>> Load<T>(ResourceKey key,bool prepared) where T : class
        {
            if (!PlayerLoopHelper.IsMainThread) return Reject<IProviderOperation<IProviderAsset<T>>>("yoo.thread", ErrorKind.InvalidState);
            if (stopped) return Reject<IProviderOperation<IProviderAsset<T>>>("yoo.stopped", ErrorKind.NotReady);
            if (!key.IsValid || key.Kind != ResourceKind.Asset || key.AssetType != typeof(T) ||
                key.Provider != Id || key.Package != backend.Name || key.ContentVersion != version)
                return Reject<IProviderOperation<IProviderAsset<T>>>("yoo.key", ErrorKind.InvalidArgument);
            if (!typeof(Object).IsAssignableFrom(typeof(T)))
                return Reject<IProviderOperation<IProviderAsset<T>>>("yoo.type", ErrorKind.NotSupported);
            try
            {
                var ready = backend.ValidateVersion(version);
                if (!ready.IsSuccess) return Result<IProviderOperation<IProviderAsset<T>>>.Failure(ready.Error);
                if(prepared&&!(backend is IYooDownloadPackage))return Reject<IProviderOperation<IProviderAsset<T>>>("yoo.prepared.unsupported",ErrorKind.NotSupported);
                var started = prepared?((IYooDownloadPackage)backend).LoadPrepared(key.Address,typeof(T)):backend.Load(key.Address, typeof(T));
                if (!started.IsSuccess) return Result<IProviderOperation<IProviderAsset<T>>>.Failure(started.Error);
                if (started.Value == null) return Reject<IProviderOperation<IProviderAsset<T>>>("yoo.handle.null", ErrorKind.Integrity);
                var handle = started.Value;
                handles.Add(handle); loading++; LoadStartCount++;
                var operation = new ProviderOperation<IProviderAsset<T>>();
                operation.Retain(CompleteLoad(key, handle, operation));
                return Result<IProviderOperation<IProviderAsset<T>>>.Success(operation);
            }
            catch (Exception e) { return Reject<IProviderOperation<IProviderAsset<T>>>("yoo.load.start", ErrorKind.Provider, e.Message); }
        }
        private async UniTask CompleteLoad<T>(ResourceKey key, IYooHandle handle, ProviderOperation<IProviderAsset<T>> operation) where T : class
        {
            Result<IProviderAsset<T>> result;
            try
            {
                while (!handle.IsDone) await UniTask.Yield();
                if (!handle.Succeeded) result = Reject<IProviderAsset<T>>("yoo.load.failed", ErrorKind.Provider, handle.Error);
                else if (!(handle.Value is T) || handle.Value == null)
                    result = Reject<IProviderAsset<T>>("yoo.load.payload", ErrorKind.Integrity);
                else result = Result<IProviderAsset<T>>.Success(new Asset<T>(this, key, handle));
            }
            catch (Exception e) { result = Reject<IProviderAsset<T>>("yoo.load.exception", ErrorKind.Provider, e.Message); }
            if (!result.IsSuccess)
            {
                var cleanup = TryRelease(handle);
                if (!cleanup.IsSuccess) { failedLoads.Add(handle); result = Result<IProviderAsset<T>>.Failure(cleanup.Error); }
            }
            loading--;
            operation.Complete(result);
        }
        private Result TryRelease(IYooHandle handle)
        {
            try
            {
                handle.Release();
                handles.Remove(handle); failedLoads.Remove(handle);
                return Result.Success();
            }
            catch (Exception e) { return Fail("yoo.release.failed", ErrorKind.CleanupFailed, e.Message); }
        }
        public Result<IProviderOperation<IProviderScene>> BeginLoadScene(ResourceKey key, SceneLoadOptions options) =>
            Reject<IProviderOperation<IProviderScene>>("yoo.scene.not_implemented", ErrorKind.NotSupported);
        private sealed class Asset<T> : IProviderAsset<T> where T : class
        {
            private readonly YooAssetProvider owner;
            private IYooHandle handle;
            private ProviderOperation<Unit> release;
            public ResourceKey Key { get; }
            public AssetLeaseState State { get; private set; } = AssetLeaseState.Active;
            public T Value => State == AssetLeaseState.Active ? (T)(object)handle.Value : throw new InvalidOperationException("Asset is not active.");
            internal Asset(YooAssetProvider owner, ResourceKey key, IYooHandle handle) { this.owner = owner; Key = key; this.handle = handle; }
            public Result<IProviderOperation<Unit>> BeginRelease(CleanupContext cleanup) => Release(false);
            public Result<IProviderOperation<Unit>> RetryRelease(CleanupContext cleanup) => Release(true);
            private Result<IProviderOperation<Unit>> Release(bool retry)
            {
                if (!PlayerLoopHelper.IsMainThread) return Reject<IProviderOperation<Unit>>("yoo.thread", ErrorKind.InvalidState);
                if (State == AssetLeaseState.Released || (retry && State != AssetLeaseState.ReleaseFailed))
                    return Reject<IProviderOperation<Unit>>("yoo.release.state", ErrorKind.InvalidState);
                if (State == AssetLeaseState.Releasing || (State == AssetLeaseState.ReleaseFailed && !retry))
                    return Result<IProviderOperation<Unit>>.Success(release);
                var attempt = new ProviderOperation<Unit>(); release = attempt; State = AssetLeaseState.Releasing;
                var result = owner.TryRelease(handle);
                State = result.IsSuccess ? AssetLeaseState.Released : AssetLeaseState.ReleaseFailed;
                if (result.IsSuccess) handle = null;
                attempt.Complete(result.IsSuccess ? Result<Unit>.Success(default) : Result<Unit>.Failure(result.Error));
                return Result<IProviderOperation<Unit>>.Success(attempt);
            }
        }
        internal static Result Fail(string code, ErrorKind kind, string message = "") => Result.Failure(new FrameworkError(kind, code, message));
        internal static Result<T> Reject<T>(string code, ErrorKind kind, string message = "") => Result<T>.Failure(new FrameworkError(kind, code, message));
    }
    internal sealed class ProviderOperation<T> : IProviderOperation<T>
    {
        private readonly UniTaskCompletionSource<Result<T>> completion = new UniTaskCompletionSource<Result<T>>();
        private UniTask driver;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public BackendState State { get; private set; } = BackendState.Running;
        public UniTask<Result<T>> Completion => completion.Task;
        internal void Retain(UniTask task) { driver = task.Preserve(); }
        internal void Complete(Result<T> result) { State = result.IsSuccess ? BackendState.Succeeded : BackendState.Failed; completion.TrySetResult(result); }
    }
}
