using System;
using NUnit.Framework;

namespace MiniGame.Framework.Tests
{
    public sealed class FoundationTests
    {
        [Test]
        public void ReusedTimerSlotRejectsOldHandle()
        {
            using (var timers = new TimerService(1))
            {
                TimerHandle old = timers.Schedule(1, () => { });
                Assert.IsTrue(timers.Cancel(old));
                TimerHandle next = timers.Schedule(1, () => { });
                Assert.IsFalse(timers.Cancel(old));
                Assert.IsTrue(next.IsActive);
            }
        }

        [Test]
        public void TimerCreatedInsideCallbackWaitsForNextTick()
        {
            using (var timers = new TimerService(2))
            {
                int calls = 0;
                timers.Schedule(0, () => { calls++; timers.Schedule(0, () => calls++); });
                timers.Tick(0, 0);
                Assert.AreEqual(1, calls);
                timers.Tick(0, 0);
                Assert.AreEqual(2, calls);
            }
        }

        [Test]
        public void RepeatingTimerDoesNotCatchUpAfterLongFrame()
        {
            using (var timers = new TimerService())
            {
                int calls = 0;
                timers.Schedule(1, () => calls++, interval: 1);
                timers.Tick(100, 100);
                Assert.AreEqual(1, calls);
                timers.Tick(0, 0);
                Assert.AreEqual(1, calls);
                timers.Tick(1, 1);
                Assert.AreEqual(2, calls);
            }
        }

        [Test]
        public void ScaledAndUnscaledTimersAreIndependent()
        {
            using (var timers = new TimerService())
            {
                int scaled = 0, unscaled = 0;
                timers.Schedule(1, () => scaled++);
                timers.Schedule(1, () => unscaled++, true);
                timers.Tick(0, 2);
                Assert.AreEqual(0, scaled);
                Assert.AreEqual(1, unscaled);
            }
        }

        [Test]
        public void CallbackBudgetLeavesRemainingWorkForLater()
        {
            using (var timers = new TimerService(4))
            {
                int calls = 0;
                for (int i = 0; i < 4; i++) timers.Schedule(0, () => calls++);
                timers.Tick(0, 0, 2);
                Assert.AreEqual(2, calls);
                timers.Tick(0, 0, 2);
                Assert.AreEqual(4, calls);
            }
        }

        [Test]
        public void CallbackExceptionCanBeObservedWithoutDroppingOtherTimers()
        {
            using (var timers = new TimerService())
            {
                int errors = 0, calls = 0;
                timers.CallbackFailed += _ => errors++;
                timers.Schedule(0, () => throw new InvalidOperationException());
                timers.Schedule(0, () => calls++);
                timers.Tick(0, 0);
                Assert.AreEqual(1, errors);
                Assert.AreEqual(1, calls);
            }
        }

        [Test]
        public void TimerCapacityAndInvalidTimeAreRejected()
        {
            using (var timers = new TimerService(1))
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => timers.Schedule(double.NaN, () => { }));
                timers.Schedule(1, () => { });
                Assert.Throws<InvalidOperationException>(() => timers.Schedule(1, () => { }));
            }
        }

        [Test]
        public void PauseOwnersReleaseOnlyThemselves()
        {
            using (var gate = new PauseGate())
            {
                IDisposable a = gate.Acquire();
                IDisposable b = gate.Acquire();
                a.Dispose(); a.Dispose();
                Assert.IsTrue(gate.IsPaused);
                b.Dispose();
                Assert.IsFalse(gate.IsPaused);
            }
        }

        [Test]
        public void StateTransitionIsExplicitAndDuplicateIsNoOp()
        {
            using (var machine = new StateMachine<int>())
            {
                int enters = 0, exits = 0;
                machine.Add(1, () => enters++, () => exits++);
                machine.Add(2, () => enters++);
                machine.Change(1); machine.Change(1); machine.Change(2);
                Assert.AreEqual(2, enters);
                Assert.AreEqual(1, exits);
                Assert.AreEqual(2, machine.Current);
            }
        }

        [Test]
        public void FailedEnterLeavesMachineStopped()
        {
            using (var machine = new StateMachine<int>())
            {
                machine.Add(1, () => throw new InvalidOperationException());
                Assert.Throws<InvalidOperationException>(() => machine.Change(1));
                Assert.IsFalse(machine.IsRunning);
            }
        }

        [Test]
        public void PoolCapacityAndDuplicateReturnsAreChecked()
        {
            using (var pool = new ObjectPool<object>(1, () => new object()))
            {
                Assert.IsTrue(pool.TryRent(out object first));
                Assert.IsFalse(pool.TryRent(out _));
                Assert.IsTrue(pool.Return(first));
                Assert.IsFalse(pool.Return(first));
                Assert.IsTrue(pool.TryRent(out object second));
                Assert.AreSame(first, second);
            }
        }

        [Test]
        public void TimerBudgetDoesNotStarveHigherSlots()
        {
            using (var timers = new TimerService(2))
            {
                int once = 0;
                timers.Schedule(0, () => { }, interval: 1);
                timers.Schedule(0, () => once++);
                timers.Tick(1, 1, 1);
                timers.Tick(1, 1, 1);
                Assert.AreEqual(1, once);
            }
        }

        [Test]
        public void PoolFactoryCannotRentRecursively()
        {
            ObjectPool<object> pool = null;
            pool = new ObjectPool<object>(1, () => { pool.TryRent(out _); return new object(); });
            Assert.Throws<InvalidOperationException>(() => pool.TryRent(out _));
            Assert.AreEqual(0, pool.Count);
            pool.Dispose();
        }

        [Test]
        public void DisposeInsideResetDestroysReturningObject()
        {
            ObjectPool<object> pool = null;
            int destroyed = 0;
            pool = new ObjectPool<object>(1, () => new object(), _ => pool.Dispose(), _ => destroyed++);
            pool.TryRent(out object item);
            Assert.IsFalse(pool.Return(item));
            Assert.AreEqual(1, destroyed);
            Assert.AreEqual(0, pool.Count);
        }

        [Test]
        public void PoolShutdownAttemptsEveryRelease()
        {
            int destroyed = 0;
            var pool = new ObjectPool<object>(2, () => new object(), destroy: _ =>
            {
                destroyed++;
                throw new InvalidOperationException();
            });
            pool.TryRent(out object a); pool.TryRent(out _); pool.Return(a);
            Assert.Throws<AggregateException>(() => pool.Dispose());
            Assert.AreEqual(2, destroyed);
            Assert.AreEqual(0, pool.Count);
            Assert.DoesNotThrow(() => pool.Dispose());
        }
    }
}
