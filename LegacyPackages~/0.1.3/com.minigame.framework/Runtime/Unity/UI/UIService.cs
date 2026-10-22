using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MiniGame.Framework
{
    public sealed class PanelHandle
    {
        internal UIService Owner;
        internal InstanceLease Lease;
        internal string Key;
        internal double Expires;
        public PanelView View { get; internal set; }
        public bool IsOpen { get; internal set; }
    }

    public sealed class UIService : IDisposable
    {
        private readonly ResourceService resources;
        private readonly Transform root;
        private readonly int cacheCapacity;
        private readonly List<PanelHandle> open = new List<PanelHandle>();
        private readonly List<PanelHandle> cached = new List<PanelHandle>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool disposed;
        private bool reordering;
        private bool layoutDirty;
        public int OpenCount => open.Count;
        public int CachedCount => cached.Count;
        public PanelHandle Focused { get; private set; }
        public event Action<PanelHandle> FocusChanged;
        public UIService(ResourceService resources, Transform root, int cacheCapacity = 0)
        {
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
            if (!root) throw new ArgumentNullException(nameof(root));
            if (cacheCapacity < 0) throw new ArgumentOutOfRangeException(nameof(cacheCapacity));
            this.root = root; this.cacheCapacity = cacheCapacity;
        }

        public async UniTask<PanelHandle> OpenAsync(string key, CancellationToken cancellation = default)
        {
            Guard();
            cancellation.ThrowIfCancellationRequested();
            Tick();
            for (int i = 0; i < cached.Count; i++)
            {
                if (cached[i].Key != key) continue;
                PanelHandle stored = cached[i];
                cached.RemoveAt(i);
                if (!stored.View) { Release(stored); break; }
                var result = new PanelHandle { Owner = this, Lease = stored.Lease, Key = key, View = stored.View };
                stored.Lease = null;
                Attach(result);
                return result;
            }
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token))
            {
                InstanceLease instance = await resources.InstantiateAsync(key, root, linked.Token);
                if (disposed || linked.IsCancellationRequested || !root)
                {
                    instance.Dispose();
                    throw new OperationCanceledException(linked.Token);
                }
                PanelView view = instance.Instance.GetComponent<PanelView>();
                if (!view) { instance.Dispose(); throw new InvalidOperationException("Panel prefab needs a PanelView."); }
                var handle = new PanelHandle { Owner = this, Lease = instance, Key = key, View = view };
                Attach(handle);
                return handle;
            }
        }

        public bool Close(PanelHandle handle)
        {
            if (disposed || handle == null || !ReferenceEquals(handle.Owner, this) || !open.Remove(handle)) return false;
            var input = UnityEngine.EventSystems.EventSystem.current;
            if (input && ReferenceEquals(Focused, handle) && handle.View) handle.View.RememberSelection(input.currentSelectedGameObject);
            handle.IsOpen = false;
            handle.Owner = null;
            if (handle.View && handle.View.CacheSeconds > 0 && cached.Count < cacheCapacity)
            {
                cached.Add(new PanelHandle { Lease = handle.Lease, Key = handle.Key, View = handle.View,
                    Expires = Time.realtimeSinceStartupAsDouble + handle.View.CacheSeconds });
                handle.Lease = null;
                handle.View.gameObject.SetActive(false);
            }
            else Release(handle);
            Reorder();
            return true;
        }
        public bool Back() => open.Count != 0 && Close(open[open.Count - 1]);
        public void Tick()
        {
            if (disposed) return;
            for (int i = cached.Count - 1; i >= 0; i--)
            {
                if (cached[i].View && Time.realtimeSinceStartupAsDouble < cached[i].Expires) continue;
                Release(cached[i]); cached.RemoveAt(i);
            }
            if (layoutDirty) Reorder();
        }
        public void CloseAll()
        {
            PanelHandle[] closing = open.ToArray();
            PanelHandle[] releasing = cached.ToArray();
            open.Clear(); cached.Clear();
            foreach (PanelHandle handle in closing) Release(handle);
            foreach (PanelHandle handle in releasing) Release(handle);
            SetFocus(null);
            if (!disposed && open.Count != 0) Reorder();
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            CloseAll();
            lifetime.Dispose();
        }
        private void Attach(PanelHandle handle)
        {
            handle.IsOpen = true;
            int index = open.Count;
            for (int i = 0; i < open.Count; i++)
                if (open[i].View && open[i].View.Layer > handle.View.Layer) { index = i; break; }
            open.Insert(index, handle);
            bool covered = false;
            for (int i = index + 1; i < open.Count; i++)
                if (open[i].IsOpen && open[i].View && open[i].View.OpaqueCover) { covered = true; break; }
            handle.View.SetCovered(covered);
            Reorder();
            if (disposed || !handle.IsOpen || !handle.View)
            {
                open.Remove(handle);
                Release(handle);
                throw new InvalidOperationException("Panel was closed during its opening callbacks.");
            }
        }
        private void Reorder()
        {
            if (disposed) return;
            if (reordering) { layoutDirty = true; return; }
            reordering = true;
            try
            {
                for (int pass = 0; pass < 16 && !disposed; pass++)
                {
                    layoutDirty = false;
                    bool covered = false;
                    PanelHandle[] snapshot = open.ToArray();
                    for (int i = snapshot.Length - 1; i >= 0 && !disposed && !layoutDirty; i--)
                    {
                        PanelHandle handle = snapshot[i];
                        if (!handle.IsOpen || !handle.View) continue;
                        handle.View.transform.SetSiblingIndex(i);
                        var input = UnityEngine.EventSystems.EventSystem.current;
                        if (covered && input && ReferenceEquals(Focused, handle)) handle.View.RememberSelection(input.currentSelectedGameObject);
                        handle.View.SetCovered(covered);
                        if (handle.IsOpen && handle.View && handle.View.OpaqueCover) covered = true;
                    }
                    if (!disposed && !layoutDirty)
                    {
                        PanelHandle next = null;
                        for (int i = open.Count - 1; i >= 0; i--)
                            if (open[i].IsOpen && open[i].View && open[i].View.isActiveAndEnabled) { next = open[i]; break; }
                        SetFocus(next);
                    }
                    if (!layoutDirty) break;
                }
            }
            finally { reordering = false; }
            if (Focused != null && (!Focused.IsOpen || !Focused.View)) Focused = null;
        }
        private void SetFocus(PanelHandle next)
        {
            if (ReferenceEquals(Focused, next)) return;
            var input = UnityEngine.EventSystems.EventSystem.current;
            if (input && Focused?.View) Focused.View.RememberSelection(input.currentSelectedGameObject);
            Focused = next;
            if (input)
            {
                GameObject selected = next?.View ? next.View.Selection : null;
                if (selected && (!selected.activeInHierarchy || !selected.transform.IsChildOf(next.View.transform))) selected = null;
                input.SetSelectedGameObject(selected);
            }
            if (!ReferenceEquals(Focused, next) || layoutDirty || (next != null && (!next.IsOpen || !next.View))) return;
            try { FocusChanged?.Invoke(next); }
            catch (Exception error) { Debug.LogException(error); }
        }

        private static void Release(PanelHandle handle)
        {
            handle.IsOpen = false;
            handle.Owner = null;
            handle.Lease?.Dispose();
            handle.Lease = null;
        }
        private void Guard()
        {
            if (disposed) throw new ObjectDisposedException(nameof(UIService));
            if (!root) throw new InvalidOperationException("UI root no longer exists.");
        }
    }
}
