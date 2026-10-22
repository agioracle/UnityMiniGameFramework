using System;
using System.Collections;
using System.Collections.Generic;
using MiniGame.Framework.Reactive;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Framework.Tests
{
    public sealed class ReactiveSemanticsTests
    {
        [Test]
        public void SubjectReentrantDeliveryAndDisposalUseActualR3()
        {
            using (var source = new Subject<int>())
            {
                var values = new List<int>();
                IDisposable observer = source.Subscribe(value =>
                {
                    values.Add(value);
                    if (value == 1) source.OnNext(2);
                });
                source.OnNext(1);
                CollectionAssert.AreEqual(new[] { 1, 2 }, values);
                observer.Dispose();
                source.OnNext(3);
                Assert.AreEqual(2, values.Count);
            }
        }

        [Test]
        public void CompletedStreamDoesNotDeliverLaterValues()
        {
            using (var source = new Subject<int>())
            {
                int values = 0, completions = 0;
                using (source.Subscribe(_ => values++, _ => completions++))
                {
                    source.OnNext(1);
                    source.OnCompleted();
                    source.OnNext(2);
                    Assert.AreEqual(1, values);
                    Assert.AreEqual(1, completions);
                }
            }
        }

        [UnityTest]
        public IEnumerator InitialRenderDisablingViewDoesNotRetainSubscription()
        {
            var go = new GameObject("Initial render lifecycle test");
            var display = go.AddComponent<DisplayLifetime>();
            using (var state = new ReactiveProperty<int>(1))
            {
                int calls = 0;
                try
                {
                    display.Bind(state, value => { calls++; go.SetActive(false); });
                    Assert.AreEqual(1, calls);
                    state.Value = 2;
                    Assert.AreEqual(1, calls);
                    go.SetActive(true);
                    display.Bind(state, value => calls++);
                    Assert.AreEqual(2, calls);
                    yield return null;
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
        }
    }
}
