using System;
using System.IO;
using MiniGame.Framework;
using NUnit.Framework;
using UnityEngine;

namespace MiniGame.Framework.Compatibility
{
    public sealed class ConsumerBaselineV010
    {
        [Serializable]
        public sealed class ConsumerData { public int value; }

        [Test]
        public void FrozenConsumerUsesPublicFoundationServices()
        {
            using (var timers = new TimerService(4))
            using (var pauses = new PauseGate())
            using (var machine = new StateMachine<string>())
            using (var pool = new ObjectPool<object>(1, () => new object()))
            {
                int fired = 0;
                TimerHandle handle = timers.Schedule(0.5, () => fired++);
                IDisposable owner = pauses.Acquire();
                timers.Tick(pauses.IsPaused ? 0 : 0.5, 0.5);
                Assert.IsTrue(handle.IsActive);
                owner.Dispose();
                timers.Tick(0.5, 0.5);
                Assert.AreEqual(1, fired);
                Assert.IsFalse(handle.IsActive);
                machine.Add("ready"); machine.Add("running");
                machine.Change("ready"); machine.Change("running");
                Assert.AreEqual("running", machine.Current);
                Assert.IsTrue(pool.TryRent(out object item));
                Assert.IsTrue(pool.Return(item));
                Assert.IsFalse(pool.Return(item));
            }
        }

        [Test]
        public void FrozenConsumerCanRoundTripItsOwnSaveSchema()
        {
            string directory = Path.Combine(Application.temporaryCachePath, "compatibility-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new LocalStore(directory, 4096);
                var data = new ConsumerData { value = 23 };
                string json = JsonCodec.Encode(data);
                Assert.AreEqual(23, JsonCodec.Decode<ConsumerData>(json).value);
                StoreResult<bool> saved = store.Save("consumer", 1, data);
                Assert.AreEqual(StoreStatus.Success, saved.Status);
                StoreResult<ConsumerData> loaded = store.Load<ConsumerData>("consumer", 1);
                Assert.IsTrue(loaded.Success);
                Assert.AreEqual(23, loaded.Value.value);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
