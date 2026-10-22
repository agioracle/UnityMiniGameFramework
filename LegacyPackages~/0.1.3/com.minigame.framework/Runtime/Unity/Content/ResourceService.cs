using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace MiniGame.Framework
{
    public sealed class ResourceService
    {
        private readonly HashSet<IDisposable> leases = new HashSet<IDisposable>();
        private ResourcePackage package;
        private bool busy;
        private bool ready;
        private int pending;
        public bool IsReady => ready && !busy;
        public int LeaseCount => leases.Count;
        public int PendingCount => pending;
        public string Version { get; private set; }
        private string[] validatedAssets = Array.Empty<string>();
        private readonly int downloadConcurrency;
        private readonly int downloadRetries;

        public ResourceService(int downloadConcurrency = 4, int downloadRetries = 2)
        {
            if (downloadConcurrency < 1 || downloadConcurrency > 64) throw new ArgumentOutOfRangeException(nameof(downloadConcurrency));
            if (downloadRetries < 0 || downloadRetries > 10) throw new ArgumentOutOfRangeException(nameof(downloadRetries));
            this.downloadConcurrency = downloadConcurrency;
            this.downloadRetries = downloadRetries;
        }

        public async UniTask InitializeAsync(string packageName, InitializeParameters parameters, CancellationToken cancellation = default,
            string contentVersion = null, IReadOnlyList<string> requiredAssets = null)
        {
            if (package != null || busy) throw new InvalidOperationException("Resource service already initialized or busy.");
            if (string.IsNullOrWhiteSpace(packageName)) throw new ArgumentException("Package name required.", nameof(packageName));
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            if (contentVersion != null && string.IsNullOrWhiteSpace(contentVersion)) throw new ArgumentException("Version cannot be empty.", nameof(contentVersion));
            string[] required = CopyKeys(requiredAssets);
            cancellation.ThrowIfCancellationRequested();
            busy = true;
            try
            {
                YooAssets.Initialize();
                if (YooAssets.TryGetPackage(packageName) != null) throw new InvalidOperationException("Package name is already owned.");
                package = YooAssets.CreatePackage(packageName);
                await Complete(package.InitializeAsync(parameters));
                cancellation.ThrowIfCancellationRequested();
                if (contentVersion == null)
                {
                    var version = package.RequestPackageVersionAsync();
                    await Complete(version);
                    contentVersion = version.PackageVersion;
                }
                cancellation.ThrowIfCancellationRequested();
                await Complete(package.UpdatePackageManifestAsync(contentVersion));
                await ValidateContentAsync(required, cancellation);
                cancellation.ThrowIfCancellationRequested();
                validatedAssets = required;
                Version = contentVersion;
                ready = true;
            }
            catch
            {
                if (package != null)
                {
                    await Complete(package.DestroyAsync());
                    YooAssets.RemovePackage(package);
                    package = null;
                }
                throw;
            }
            finally { busy = false; }
        }

        public async UniTask<AssetLease<T>> LoadAsync<T>(string key, CancellationToken cancellation = default) where T : UnityEngine.Object
        {
            RequireReady();
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Asset key required.", nameof(key));
            cancellation.ThrowIfCancellationRequested();
            pending++;
            AssetHandle handle = null;
            try
            {
                if (package.GetAssetInfo(key).IsInvalid) throw new InvalidOperationException("Asset is not in the manifest: " + key);
                var download = package.CreateBundleDownloader(key, true, downloadConcurrency, downloadRetries);
                download.BeginDownload();
                await Complete(download);
                cancellation.ThrowIfCancellationRequested();
                handle = package.LoadAssetAsync<T>(key);
                while (!handle.IsDone) await UniTask.Yield();
                cancellation.ThrowIfCancellationRequested();
                if (handle.Status != EOperationStatus.Succeed) throw new InvalidOperationException(handle.LastError);
                if (!handle.GetAssetObject<T>()) throw new InvalidOperationException("Loaded asset has an unexpected type.");
                var lease = new AssetLease<T>(handle, this);
                leases.Add(lease);
                handle = null;
                return lease;
            }
            finally
            {
                handle?.Release();
                pending--;
            }
        }

        public async UniTask<InstanceLease> InstantiateAsync(string key, Transform parent = null, CancellationToken cancellation = default)
        {
            AssetLease<GameObject> lease = await LoadAsync<GameObject>(key, cancellation);
            GameObject instance = null;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                instance = UnityEngine.Object.Instantiate(lease.Asset, parent, false);
                if (instance.GetComponent<ResourceOwner>()) throw new InvalidOperationException("Prefab must not contain a resource owner.");
                var result = new InstanceLease(instance, lease);
                instance.AddComponent<ResourceOwner>().Attach(result);
                return result;
            }
            catch
            {
                if (instance) UnityEngine.Object.Destroy(instance);
                lease.Dispose();
                throw;
            }
        }

        public async UniTask<string> CheckVersionAsync(CancellationToken cancellation = default)
        {
            RequireReady();
            pending++;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                var operation = package.RequestPackageVersionAsync();
                await Complete(operation);
                cancellation.ThrowIfCancellationRequested();
                return operation.PackageVersion;
            }
            finally { pending--; }
        }

        public async UniTask ActivateVersionAsync(string version, CancellationToken cancellation = default,
            IReadOnlyList<string> requiredAssets = null)
        {
            RequireReady();
            if (leases.Count != 0 || pending != 0) throw new InvalidOperationException("Version change requires no active leases or loads.");
            if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Version required.", nameof(version));
            string[] required = CopyKeys(requiredAssets ?? validatedAssets);
            cancellation.ThrowIfCancellationRequested();
            string previousVersion = Version;
            busy = true;
            ready = false;
            try
            {
                await Complete(package.UnloadAllAssetsAsync());
                await Complete(package.UpdatePackageManifestAsync(version));
                await ValidateContentAsync(required, cancellation);
                cancellation.ThrowIfCancellationRequested();
                validatedAssets = required;
                Version = version;
                ready = true;
            }
            catch (Exception activationError)
            {
                try
                {
                    await Complete(package.UnloadAllAssetsAsync());
                    await Complete(package.UpdatePackageManifestAsync(previousVersion));
                    await ValidateContentAsync(validatedAssets, CancellationToken.None);
                    Version = previousVersion;
                    ready = true;
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("Content activation and rollback both failed. Resource service remains unavailable.", activationError, rollbackError);
                }
                throw;
            }
            finally { busy = false; }
        }

        public async UniTask DownloadAsync(string tag, int concurrency = 4, int retries = 2, CancellationToken cancellation = default)
        {
            RequireReady();
            if (concurrency < 1 || retries < 0) throw new ArgumentOutOfRangeException(nameof(concurrency));
            cancellation.ThrowIfCancellationRequested();
            pending++;
            try
            {
                var download = string.IsNullOrEmpty(tag) ? package.CreateResourceDownloader(concurrency, retries) : package.CreateResourceDownloader(tag, concurrency, retries);
                download.BeginDownload();
                await Complete(download);
                cancellation.ThrowIfCancellationRequested();
            }
            finally { pending--; }
        }

        public async UniTask ShutdownAsync()
        {
            if (busy) throw new InvalidOperationException("Resource service is busy.");
            if (pending != 0 || leases.Count != 0) throw new InvalidOperationException("Release scenes, instances and leases before shutdown.");
            if (package == null) return;
            busy = true;
            ready = false;
            try
            {
                await Complete(package.DestroyAsync());
                YooAssets.RemovePackage(package);
                package = null;
                Version = null;
            }
            finally { busy = false; }
        }

        public async UniTask CollectUnusedAsync()
        {
            RequireReady();
            if (pending != 0) throw new InvalidOperationException("Wait for in-flight operations before collecting resources.");
            busy = true;
            try { await Complete(package.UnloadUnusedAssetsAsync()); }
            finally { busy = false; }
        }

        private async UniTask ValidateContentAsync(string[] required, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (string key in required)
                if (package.GetAssetInfo(key).IsInvalid) throw new InvalidOperationException("Required content is not in the manifest: " + key);
            if (required.Length != 0)
            {
                var download = package.CreateBundleDownloader(required, true, downloadConcurrency, downloadRetries);
                download.BeginDownload();
                await Complete(download);
            }
            foreach (string key in required)
            {
                cancellation.ThrowIfCancellationRequested();
                AssetHandle handle = package.LoadAssetAsync<UnityEngine.Object>(key);
                try
                {
                    while (!handle.IsDone) await UniTask.Yield();
                    cancellation.ThrowIfCancellationRequested();
                    if (handle.Status != EOperationStatus.Succeed || !handle.AssetObject)
                        throw new InvalidOperationException("Required content is unavailable: " + key + ". " + handle.LastError);
                }
                finally { handle.Release(); }
            }
        }

        private static string[] CopyKeys(IReadOnlyList<string> keys)
        {
            string[] copy = keys == null ? Array.Empty<string>() : new string[keys.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(keys[i])) throw new ArgumentException("Required asset keys cannot be empty.", nameof(keys));
                copy[i] = keys[i];
            }
            return copy;
        }

        internal ResourcePackage BeginScene()
        {
            RequireReady();
            pending++;
            return package;
        }
        internal async UniTask DownloadSceneDependenciesAsync(string key, CancellationToken cancellation, IProgress<float> progress = null)
        {
            if (package.GetAssetInfo(key).IsInvalid) throw new InvalidOperationException("Scene is not in the manifest: " + key);
            var download = package.CreateBundleDownloader(key, true, downloadConcurrency, downloadRetries);
            download.BeginDownload();
            while (!download.IsDone)
            {
                progress?.Report(download.Progress * 0.5f);
                await UniTask.Yield();
            }
            if (download.Status != EOperationStatus.Succeed) throw new InvalidOperationException(download.Error);
            cancellation.ThrowIfCancellationRequested();
            progress?.Report(0.5f);
        }
        internal void EndSceneLoad() { pending--; }
        internal void Retain(IDisposable lease) { leases.Add(lease); }
        internal void Release(IDisposable lease) { leases.Remove(lease); }
        internal static async UniTask Complete(AsyncOperationBase operation)
        {
            while (!operation.IsDone) await UniTask.Yield();
            if (operation.Status != EOperationStatus.Succeed) throw new InvalidOperationException(operation.Error);
        }
        private void RequireReady()
        {
            if (!IsReady) throw new InvalidOperationException("Resource service is not ready.");
        }
    }
}
