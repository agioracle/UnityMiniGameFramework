using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniGame.Framework.Reactive;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Framework.Tests
{
    public sealed class RuntimeLifecycleTests
    {
        [UnityTest]
        public IEnumerator RuntimeInitializesAndStopsIdempotently()
        {
            var runtime = new FrameworkRuntime();
            var task = runtime.InitializeAsync(new FrameworkOptions()).AsTask();
            while (!task.IsCompleted) yield return null;
            Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());
            Assert.AreEqual(FrameworkRuntime.Lifecycle.Ready, runtime.State);
            int calls = 0;
            runtime.Timers.Schedule(0, () => calls++);
            runtime.Tick(0, 0);
            Assert.AreEqual(1, calls);
            runtime.Dispose(); runtime.Dispose();
            Assert.AreEqual(FrameworkRuntime.Lifecycle.Stopped, runtime.State);
            Assert.AreEqual(0, runtime.Timers.Count);
        }

        [UnityTest]
        public IEnumerator CancellationRollsBackInitialization()
        {
            var runtime = new FrameworkRuntime();
            using (var cancellation = new CancellationTokenSource())
            {
                var task = runtime.InitializeAsync(new FrameworkOptions(), cancellation.Token).AsTask();
                cancellation.Cancel();
                while (!task.IsCompleted) yield return null;
                Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
                Assert.AreEqual(FrameworkRuntime.Lifecycle.Stopped, runtime.State);
                Assert.AreEqual(0, runtime.Timers.Count);
            }
        }

        [UnityTest]
        public IEnumerator DisposeDuringInitializationDoesNotBecomeReady()
        {
            var runtime = new FrameworkRuntime();
            var task = runtime.InitializeAsync(new FrameworkOptions()).AsTask();
            runtime.Dispose();
            while (!task.IsCompleted) yield return null;
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.AreEqual(FrameworkRuntime.Lifecycle.Stopped, runtime.State);
        }

        [UnityTest]
        public IEnumerator DisplaySubscriptionsEndOnDisableAndRebindToCurrentValue()
        {
            var go = new GameObject("Display subscription test");
            var display = go.AddComponent<DisplayLifetime>();
            var state = new ReactiveProperty<int>(2);
            int current = 0;
            try
            {
                display.Bind(state, x => current = x);
                Assert.AreEqual(2, current);
                go.SetActive(false);
                state.Value = 3;
                Assert.AreEqual(2, current);
                go.SetActive(true);
                display.Bind(state, x => current = x);
                Assert.AreEqual(3, current);
                yield return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                state.Dispose();
            }
        }
    }
}
