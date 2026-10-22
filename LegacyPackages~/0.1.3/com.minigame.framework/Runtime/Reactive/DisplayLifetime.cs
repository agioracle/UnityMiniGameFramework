using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace MiniGame.Framework.Reactive
{
    public sealed class DisplayLifetime : MonoBehaviour
    {
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();
        private ulong generation;
        private int threadId;
        private void Awake() => threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

        public void Bind<T>(Observable<T> source, Action<T> render)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != threadId) throw new InvalidOperationException("Display bindings must be created on the Unity thread.");
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (render == null) throw new ArgumentNullException(nameof(render));
            if (!isActiveAndEnabled) throw new InvalidOperationException("Bind only while the display is enabled.");
            ulong version = generation;
            IDisposable subscription = source.Subscribe(value =>
            {
                if (System.Threading.Thread.CurrentThread.ManagedThreadId != threadId)
                    throw new InvalidOperationException("Dispatch display values on the Unity thread before binding.");
                if (this && isActiveAndEnabled && version == generation) render(value);
            });
            if (!this || !isActiveAndEnabled || version != generation) subscription.Dispose();
            else subscriptions.Add(subscription);
        }

        public void Clear()
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != threadId) throw new InvalidOperationException("Display bindings must be cleared on the Unity thread.");
            generation++;
            IDisposable[] releasing = subscriptions.ToArray();
            subscriptions.Clear();
            foreach (IDisposable subscription in releasing)
            {
                try { subscription.Dispose(); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }

        private void OnDisable() => Clear();
        private void OnDestroy() => Clear();
    }
}
