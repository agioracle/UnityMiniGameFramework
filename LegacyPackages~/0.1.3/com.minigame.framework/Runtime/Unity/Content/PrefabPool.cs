using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MiniGame.Framework
{
    public readonly struct PooledHandle
    {
        internal readonly PrefabPool Owner;
        internal readonly int Id;
        internal readonly ulong Generation;
        internal PooledHandle(PrefabPool owner, int id, ulong generation) { Owner = owner; Id = id; Generation = generation; }
        public GameObject Instance => Owner != null ? Owner.Resolve(this) : throw new InvalidOperationException("Empty pool handle.");
    }

    public sealed class PrefabPool
    {
        private sealed class Entry
        {
            public GameObject Instance;
            public ulong Generation;
            public bool Rented;
            public bool Returning;
        }
        private readonly List<Entry> entries = new List<Entry>();
        private readonly AssetLease<GameObject> prefab;
        private readonly Transform root;
        private readonly GameObject staging;
        private readonly int capacity;
        private bool closed;
        private bool prewarming;
        private UniTaskCompletionSource closing;
        public int Count => entries.Count;
        public int RentedCount { get; private set; }

        private PrefabPool(AssetLease<GameObject> prefab, Transform root, int capacity)
        {
            this.prefab = prefab; this.root = root; this.capacity = capacity;
            staging = new GameObject("Pool staging");
            staging.SetActive(false);
            staging.transform.SetParent(root, false);
        }

        public static async UniTask<PrefabPool> CreateAsync(ResourceService resources, string key, Transform root, int capacity, CancellationToken cancellation = default)
        {
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            if (!root) throw new ArgumentNullException(nameof(root));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            var lease = await resources.LoadAsync<GameObject>(key, cancellation);
            if (!root) { lease.Dispose(); throw new InvalidOperationException("Pool root was destroyed while loading."); }
            return new PrefabPool(lease, root, capacity);
        }

        public async UniTask PrewarmAsync(int count, CancellationToken cancellation = default)
        {
            Guard();
            if (prewarming) throw new InvalidOperationException("Pool is already prewarming.");
            if (count < 0 || count > capacity) throw new ArgumentOutOfRangeException(nameof(count));
            prewarming = true;
            try
            {
                while (entries.Count < count)
                {
                    Guard();
                    cancellation.ThrowIfCancellationRequested();
                    CreateEntry();
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellation);
                }
            }
            finally { prewarming = false; }
        }

        public bool TryRent(out PooledHandle handle)
        {
            Guard();
            int index = -1;
            for (int i = 0; i < entries.Count; i++)
                if (!entries[i].Rented && entries[i].Generation != ulong.MaxValue) { index = i; break; }
            if (index < 0)
            {
                if (entries.Count >= capacity) { handle = default; return false; }
                index = CreateEntry();
            }
            Entry entry = entries[index];
            if (!entry.Instance) throw new InvalidOperationException("A pool instance was destroyed externally.");
            entry.Rented = true;
            entry.Generation++;
            RentedCount++;
            handle = new PooledHandle(this, index, entry.Generation);
            entry.Instance.SetActive(true);
            if (!Valid(handle)) { handle = default; return false; }
            return true;
        }

        public bool Return(PooledHandle handle)
        {
            if (!Valid(handle)) return false;
            Entry entry = entries[handle.Id];
            if (entry.Returning) return false;
            if (!entry.Instance) { entry.Rented = false; RentedCount--; return false; }
            entry.Returning = true;
            try { entry.Instance.SetActive(false); }
            finally { entry.Returning = false; }
            if (closed) return false;
            entry.Rented = false;
            RentedCount--;
            if (root) entry.Instance.transform.SetParent(root, false);
            entry.Instance.transform.localPosition = Vector3.zero;
            entry.Instance.transform.localRotation = Quaternion.identity;
            entry.Instance.transform.localScale = prefab.Asset.transform.localScale;
            return true;
        }

        public UniTask CloseAsync()
        {
            if (closing != null) return closing.Task;
            closed = true;
            var completion = new UniTaskCompletionSource();
            closing = completion;
            CloseCoreAsync(completion).Forget(Debug.LogException);
            return completion.Task;
        }
        private async UniTask CloseCoreAsync(UniTaskCompletionSource completion)
        {
            try
            {
                foreach (Entry entry in entries) if (entry.Instance) UnityEngine.Object.Destroy(entry.Instance);
                if (staging) UnityEngine.Object.Destroy(staging);
                await UniTask.NextFrame();
                entries.Clear();
                RentedCount = 0;
                prefab.Dispose();
                completion.TrySetResult();
            }
            catch (Exception error)
            {
                closing = null;
                completion.TrySetException(error);
            }
        }

        internal GameObject Resolve(PooledHandle handle)
        {
            if (!Valid(handle) || !entries[handle.Id].Instance) throw new InvalidOperationException("Expired pool handle.");
            return entries[handle.Id].Instance;
        }
        private bool Valid(PooledHandle handle) => !closed && ReferenceEquals(handle.Owner, this) && handle.Id >= 0 && handle.Id < entries.Count && entries[handle.Id].Rented && entries[handle.Id].Generation == handle.Generation;
        private int CreateEntry()
        {
            Guard();
            GameObject instance = UnityEngine.Object.Instantiate(prefab.Asset, staging.transform, false);
            instance.SetActive(false);
            instance.transform.SetParent(root, false);
            entries.Add(new Entry { Instance = instance });
            return entries.Count - 1;
        }
        private void Guard()
        {
            if (closed) throw new ObjectDisposedException(nameof(PrefabPool));
            if (!root) throw new InvalidOperationException("Pool root is no longer available.");
        }
    }
}
