using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using YooAsset;

namespace MiniGame.Framework
{
    public sealed class SceneLease : IDisposable
    {
        private SceneHandle handle;
        private readonly ResourceService resources;
        private UniTaskCompletionSource unload;
        internal SceneLease(SceneHandle handle, ResourceService resources) { this.handle = handle; this.resources = resources; }
        public Scene Scene => handle != null ? handle.SceneObject : throw new ObjectDisposedException(nameof(SceneLease));
        public UniTask UnloadAsync()
        {
            if (unload != null) return unload.Task;
            if (handle == null) return UniTask.CompletedTask;
            var completion = new UniTaskCompletionSource();
            unload = completion;
            UnloadCoreAsync(completion).Forget(UnityEngine.Debug.LogException);
            return completion.Task;
        }
        private async UniTask UnloadCoreAsync(UniTaskCompletionSource completion)
        {
            try
            {
                await ResourceService.Complete(handle.UnloadAsync());
                handle = null;
                resources.Release(this);
                completion.TrySetResult();
            }
            catch (Exception error)
            {
                unload = null;
                completion.TrySetException(error);
            }
        }
        public void Dispose() => UnloadAsync().Forget(UnityEngine.Debug.LogException);
    }

    public sealed class SceneService
    {
        private readonly ResourceService resources;
        private bool loading;
        public SceneService(ResourceService resources) { this.resources = resources ?? throw new ArgumentNullException(nameof(resources)); }
        public UniTask<SceneLease> LoadAsync(string key, CancellationToken cancellation = default)
            => LoadWithProgressAsync(key, null, cancellation);

        public async UniTask<SceneLease> LoadWithProgressAsync(string key, IProgress<float> progress, CancellationToken cancellation = default)
        {
            if (loading) throw new InvalidOperationException("Scene loads are serialized.");
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Scene key required.", nameof(key));
            cancellation.ThrowIfCancellationRequested();
            ResourcePackage package = resources.BeginScene();
            loading = true;
            SceneHandle handle = null;
            try
            {
                var reports = new SceneProgress(progress);
                reports.Report(0);
                await resources.DownloadSceneDependenciesAsync(key, cancellation, reports);
                cancellation.ThrowIfCancellationRequested();
                handle = package.LoadSceneAsync(key, LoadSceneMode.Additive);
                while (!handle.IsDone)
                {
                    reports.Report(Math.Min(0.999999f, 0.5f + handle.Progress * 0.5f));
                    await UniTask.Yield();
                }
                if (handle.Status != EOperationStatus.Succeed) throw new InvalidOperationException(handle.LastError);
                var lease = new SceneLease(handle, resources);
                resources.Retain(lease);
                handle = null;
                if (cancellation.IsCancellationRequested)
                {
                    await lease.UnloadAsync();
                    cancellation.ThrowIfCancellationRequested();
                }
                reports.Report(1);
                if (cancellation.IsCancellationRequested)
                {
                    await lease.UnloadAsync();
                    cancellation.ThrowIfCancellationRequested();
                }
                return lease;
            }
            finally
            {
                handle?.Release();
                resources.EndSceneLoad();
                loading = false;
            }
        }
    }

    internal sealed class SceneProgress : IProgress<float>
    {
        private readonly IProgress<float> target;
        private float last = -1;
        public SceneProgress(IProgress<float> target) { this.target = target; }
        public void Report(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            value = Math.Max(0, Math.Min(1, value));
            if (value <= last) return;
            last = value;
            try { target?.Report(value); }
            catch (Exception error) { UnityEngine.Debug.LogException(error); }
        }
    }
}
