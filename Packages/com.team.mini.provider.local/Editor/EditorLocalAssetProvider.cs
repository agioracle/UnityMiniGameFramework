using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using UnityEditor;
using Object = UnityEngine.Object;

namespace MiniFramework.Providers.Local
{
    // Editor only. Addresses are explicit, exact mappings; no suffix search or Resources fallback.
    public sealed class EditorLocalAssetProvider : IAssetProvider, ISceneProvider
    {
        private readonly Dictionary<string, string> paths;
        private readonly string package, version;
        public string Id { get; }
        public int LoadStartCount { get; private set; }
        public int RetainedHandleCount { get; private set; }
        public bool SupportsDeferredActivation => false;
        public EditorLocalAssetProvider(string package, string contentVersion, IReadOnlyDictionary<string, string> addresses, string id = "editor-local")
        {
            if (string.IsNullOrWhiteSpace(package) || string.IsNullOrWhiteSpace(contentVersion) || string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Frozen package/version/provider required.");
            if (addresses == null) throw new ArgumentNullException(nameof(addresses));
            this.package = package; version = contentVersion; Id = id;
            paths = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in addresses)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || !ValidPath(pair.Value)) throw new ArgumentException("Exact Assets/ path required.");
                paths.Add(pair.Key, pair.Value);
            }
        }
        private static bool ValidPath(string path) => !string.IsNullOrWhiteSpace(path) && path.StartsWith("Assets/", StringComparison.Ordinal) &&
            !path.Contains("\\") && !path.Contains("//") && !path.Contains("/../") && !path.Contains("/./") && !path.EndsWith("/..") && !path.EndsWith("/.");
        public Result<IProviderOperation<IProviderAsset<T>>> BeginLoad<T>(ResourceKey key) where T : class
        {
            if (!PlayerLoopHelper.IsMainThread) return Reject<IProviderOperation<IProviderAsset<T>>>("local.thread", ErrorKind.InvalidState);
            if (!key.IsValid || key.Kind != ResourceKind.Asset || key.AssetType != typeof(T) || key.Provider != Id || key.Package != package || key.ContentVersion != version)
                return Reject<IProviderOperation<IProviderAsset<T>>>("local.key", ErrorKind.InvalidArgument);
            if (!typeof(Object).IsAssignableFrom(typeof(T))) return Reject<IProviderOperation<IProviderAsset<T>>>("local.type", ErrorKind.NotSupported);
            if (!paths.TryGetValue(key.Address, out var path)) return Reject<IProviderOperation<IProviderAsset<T>>>("local.address.missing", ErrorKind.Unavailable);
            var operation = new LocalOperation<IProviderAsset<T>>(); LoadStartCount++;
            operation.Retain(Load(key, path, operation));
            return Result<IProviderOperation<IProviderAsset<T>>>.Success(operation);
        }
        private async UniTask Load<T>(ResourceKey key, string path, LocalOperation<IProviderAsset<T>> operation) where T : class
        {
            // Preserve an actual asynchronous boundary without pretending to simulate network timing.
            await UniTask.Yield();
            Result<IProviderAsset<T>> result;
            try
            {
                var canonical = AssetDatabase.GUIDToAssetPath(AssetDatabase.AssetPathToGUID(path));
                if (canonical != path) result = Reject<IProviderAsset<T>>("local.path.missing", ErrorKind.Unavailable);
                else
                {
                    var value = AssetDatabase.LoadAssetAtPath(path, typeof(T));
                    if (value == null) result = Reject<IProviderAsset<T>>("local.payload", ErrorKind.Integrity);
                    else { RetainedHandleCount++; result = Result<IProviderAsset<T>>.Success(new Asset<T>(this, key, value)); }
                }
            }
            catch (Exception e) { result = Reject<IProviderAsset<T>>("local.load.exception", ErrorKind.Provider, e.Message); }
            operation.Complete(result);
        }
        public Result<IProviderOperation<IProviderScene>> BeginLoadScene(ResourceKey key, SceneLoadOptions options) =>
            Reject<IProviderOperation<IProviderScene>>("local.scene.not_implemented", ErrorKind.NotSupported);
        private sealed class Asset<T> : IProviderAsset<T> where T : class
        {
            private readonly EditorLocalAssetProvider owner;
            private Object value;
            public ResourceKey Key { get; }
            public AssetLeaseState State { get; private set; } = AssetLeaseState.Active;
            public T Value => State == AssetLeaseState.Active ? (T)(object)value : throw new InvalidOperationException("Asset is not active.");
            internal Asset(EditorLocalAssetProvider owner, ResourceKey key, Object value) { this.owner = owner; Key = key; this.value = value; }
            public Result<IProviderOperation<Unit>> BeginRelease(CleanupContext cleanup)
            {
                if (!PlayerLoopHelper.IsMainThread) return Reject<IProviderOperation<Unit>>("local.thread", ErrorKind.InvalidState);
                if (State != AssetLeaseState.Active) return Reject<IProviderOperation<Unit>>("local.release.state", ErrorKind.InvalidState);
                // AssetDatabase owns the imported object. Never Destroy/UnloadAsset editor-authored assets.
                value = null; State = AssetLeaseState.Released; owner.RetainedHandleCount--;
                var operation = new LocalOperation<Unit>(); operation.Complete(Result<Unit>.Success(default));
                return Result<IProviderOperation<Unit>>.Success(operation);
            }
            public Result<IProviderOperation<Unit>> RetryRelease(CleanupContext cleanup) => Reject<IProviderOperation<Unit>>("local.retry.state", ErrorKind.InvalidState);
        }
        private static Result<T> Reject<T>(string code, ErrorKind kind, string message = "") => Result<T>.Failure(new FrameworkError(kind, code, message));
        private sealed class LocalOperation<T> : IProviderOperation<T>
        {
            private readonly UniTaskCompletionSource<Result<T>> source = new UniTaskCompletionSource<Result<T>>();
            private UniTask driver;
            public string Id { get; } = Guid.NewGuid().ToString("N");
            public BackendState State { get; private set; } = BackendState.Running;
            public UniTask<Result<T>> Completion => source.Task;
            internal void Retain(UniTask task) { driver = task.Preserve(); }
            internal void Complete(Result<T> result) { State = result.IsSuccess ? BackendState.Succeeded : BackendState.Failed; source.TrySetResult(result); }
        }
    }
}
