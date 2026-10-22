using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MiniGame.Framework.Tests
{
    public sealed class PointerInputTests
    {
        [Test]
        public void UiConsumedStartNeverBecomesAGameplayTapOutsideUi()
        {
            var input = new LegacyPointerInput();
            var actions = new List<PointerAction>();
            input.Action += actions.Add;
            input.Feed(3, Vector2.zero, TouchPhase.Began, true);
            input.Feed(3, new Vector2(100, 100), TouchPhase.Moved);
            input.Feed(3, new Vector2(200, 200), TouchPhase.Ended);
            Assert.IsEmpty(actions);
            input.Feed(-1, Vector2.zero, TouchPhase.Began);
            input.Feed(-1, Vector2.one, TouchPhase.Ended);
            CollectionAssert.AreEqual(new[] { PointerGesture.Began, PointerGesture.Tapped }, actions.ConvertAll(action => action.Gesture));
        }

        [Test]
        public void DragAndCancellationPreservePointerOwnershipAndResetClearsState()
        {
            var input = new LegacyPointerInput(1, 10);
            var actions = new List<PointerAction>();
            input.Action += actions.Add;
            Assert.IsTrue(input.Feed(1, Vector2.zero, TouchPhase.Began));
            Assert.IsFalse(input.Feed(2, Vector2.zero, TouchPhase.Began));
            Assert.IsFalse(input.Feed(1, Vector2.zero, TouchPhase.Began));
            input.Feed(1, new Vector2(20, 0), TouchPhase.Moved);
            input.Feed(1, new Vector2(30, 0), TouchPhase.Ended);
            Assert.AreEqual(PointerGesture.Dragged, actions[1].Gesture);
            Assert.AreEqual(new Vector2(20, 0), actions[1].Delta);
            Assert.AreEqual(PointerGesture.Ended, actions[2].Gesture);
            input.Feed(2, Vector2.zero, TouchPhase.Began);
            input.Feed(2, Vector2.zero, TouchPhase.Canceled);
            Assert.AreEqual(PointerGesture.Cancelled, actions[4].Gesture);
            input.Feed(3, Vector2.zero, TouchPhase.Began);
            input.Reset();
            Assert.IsFalse(input.Feed(3, Vector2.zero, TouchPhase.Ended));
            Assert.IsTrue(input.Feed(4, Vector2.zero, TouchPhase.Began));
        }

        [Test]
        public void InvalidPointerParametersAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LegacyPointerInput(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LegacyPointerInput(33));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LegacyPointerInput(1, float.NaN));
            var input = new LegacyPointerInput();
            Assert.Throws<ArgumentException>(() => input.Feed(1, new Vector2(float.NaN, 0), TouchPhase.Began));
            Assert.Throws<ArgumentException>(() => input.IsConsumedByUi(1, new Vector2(float.PositiveInfinity, 0)));
        }
    }
}
