using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGame.Framework
{
    [RequireComponent(typeof(Image))]
    public sealed class UIImageBinding : MonoBehaviour
    {
        private Image image;
        private ResourceService resources;
        private AssetLease<Sprite> active;
        private CancellationTokenSource pending;
        private long generation;
        private void Awake() { image = GetComponent<Image>(); }
        public void Bind(ResourceService service) { resources = service ?? throw new ArgumentNullException(nameof(service)); }
        public async UniTask<bool> SetAsync(string key, CancellationToken cancellation = default)
        {
            if (resources == null) throw new InvalidOperationException("Bind a resource service first.");
            if (!isActiveAndEnabled) return false;
            CancelPending();
            var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            pending = request;
            long version = ++generation;
            AssetLease<Sprite> loaded = null;
            try
            {
                loaded = await resources.LoadAsync<Sprite>(key, request.Token);
                if (!this || !isActiveAndEnabled || version != generation || request.IsCancellationRequested) return false;
                AssetLease<Sprite> old = active;
                active = loaded;
                loaded = null;
                image.sprite = active.Asset;
                old?.Dispose();
                return true;
            }
            catch (OperationCanceledException) { return false; }
            finally
            {
                loaded?.Dispose();
                if (ReferenceEquals(pending, request)) pending = null;
                request.Dispose();
            }
        }
        public void Release()
        {
            CancelPending();
            generation++;
            if (image) image.sprite = null;
            active?.Dispose();
            active = null;
        }
        private void CancelPending() { pending?.Cancel(); pending = null; }
        private void OnDisable() { CancelPending(); generation++; }
        private void OnDestroy() => Release();
    }
}
