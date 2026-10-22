using System;
using YooAsset;
using UnityEngine;

namespace MiniGame.Framework
{
    public sealed class AssetLease<T> : IDisposable where T : UnityEngine.Object
    {
        private AssetHandle handle;
        private ResourceService owner;
        internal AssetLease(AssetHandle handle, ResourceService owner) { this.handle = handle; this.owner = owner; }
        public bool IsDisposed => handle == null;
        public T Asset
        {
            get
            {
                if (handle == null) throw new ObjectDisposedException(nameof(AssetLease<T>));
                return handle.GetAssetObject<T>();
            }
        }
        public void Dispose()
        {
            AssetHandle releasing = handle;
            if (releasing == null) return;
            handle = null;
            releasing.Release();
            owner.Release(this);
            owner = null;
        }
    }

    public sealed class InstanceLease : IDisposable
    {
        private readonly GameObject instance;
        private AssetLease<GameObject> asset;
        private bool disposed;
        internal InstanceLease(GameObject instance, AssetLease<GameObject> asset)
        {
            this.instance = instance;
            this.asset = asset;
            Cysharp.Threading.Tasks.UniTaskExtensions.Forget(WatchDestructionAsync(), UnityEngine.Debug.LogException);
        }
        public GameObject Instance => !disposed && instance ? instance : throw new ObjectDisposedException(nameof(InstanceLease));
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (instance) UnityEngine.Object.Destroy(instance);
        }
        internal void ReleaseDestroyed()
        {
            asset?.Dispose();
            asset = null;
        }
        private async Cysharp.Threading.Tasks.UniTask WatchDestructionAsync()
        {
            while (instance && asset != null) await Cysharp.Threading.Tasks.UniTask.NextFrame();
            ReleaseDestroyed();
        }
    }
}
