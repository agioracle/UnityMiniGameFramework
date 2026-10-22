using System;
using System.Collections;
using MiniGame.Framework.Reactive;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Framework.Tests
{
    public sealed class ReactiveBoundaryTests
    {
        [Test]
        public void ResumableErrorDoesNotSilentlyCompleteActualR3Stream()
        {
            using (var source = new Subject<int>())
            {
                int values = 0, errors = 0, completions = 0;
                var failure = new InvalidOperationException("Controlled reactive failure");
                using (source.Subscribe(_ => values++, error => { Assert.AreSame(failure, error); errors++; }, _ => completions++))
                {
                    source.OnNext(1);
                    source.OnErrorResume(failure);
                    source.OnNext(2);
                    Assert.AreEqual(2, values);
                    Assert.AreEqual(1, errors);
                    Assert.AreEqual(0, completions);
                    source.OnCompleted();
                    Assert.AreEqual(1, completions);
                }
            }
        }

        [UnityTest]
        public IEnumerator ClearDuringInitialSnapshotCannotRegisterAStaleSubscription()
        {
            var go = new GameObject("Initial snapshot clear boundary");
            var display = go.AddComponent<DisplayLifetime>();
            using (var state = new ReactiveProperty<int>(1))
            {
                int calls = 0;
                try
                {
                    display.Bind(state, _ => { calls++; display.Clear(); });
                    Assert.AreEqual(1, calls);
                    state.Value = 2;
                    Assert.AreEqual(1, calls, "Clear during the synchronous initial render must also release the subscription being created.");
                    display.Bind(state, _ => calls++);
                    Assert.AreEqual(2, calls);
                    state.Value = 3;
                    Assert.AreEqual(3, calls);
                    yield return null;
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
        }

        [UnityTest]
        public IEnumerator InitialDisableAndReenableDoesNotReuseTheOldDisplayGeneration()
        {
            var go = new GameObject("Initial snapshot reopen boundary");
            var display = go.AddComponent<DisplayLifetime>();
            using (var state = new ReactiveProperty<int>(1))
            {
                int oldCalls = 0, newCalls = 0;
                try
                {
                    display.Bind(state, _ =>
                    {
                        oldCalls++;
                        go.SetActive(false);
                        go.SetActive(true);
                        display.Bind(state, value => newCalls++);
                    });
                    Assert.AreEqual(1, oldCalls);
                    Assert.AreEqual(1, newCalls);
                    state.Value = 2;
                    Assert.AreEqual(1, oldCalls, "An old initial render must not join the reopened display lifetime.");
                    Assert.AreEqual(2, newCalls);
                    yield return null;
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
        }
    }
}
