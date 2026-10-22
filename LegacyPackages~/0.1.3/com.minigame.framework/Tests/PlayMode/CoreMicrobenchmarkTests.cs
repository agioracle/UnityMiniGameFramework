using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace MiniGame.Framework.Tests
{
    public sealed class CoreMicrobenchmarkTests
    {
        private sealed class Item { public int Value; }

        [Serializable]
        private sealed class Observation
        {
            public string loadVersion = "core-micro-v1";
            public string scope = "Synchronous Editor core microbenchmark only. No UI/audio/resources, real frame cadence, full L1/L2/L3 load or device/AOT performance verdict.";
            public string budgetState = "Not frozen: observations only";
            public string unityVersion;
            public string operatingSystem;
            public string processor;
            public int multiplier;
            public int warmupTicks = 1000;
            public int measuredTicks = 10000;
            public double logicalDeltaSeconds = 1.0 / 60;
            public int timerCount;
            public int channelCount = 16;
            public int listenersPerChannel;
            public int notificationsPerTick;
            public int poolCapacity;
            public int poolOperationsPerSecond;
            public int payloadBytes = sizeof(int);
            public long timerCallbacks;
            public long reactiveDeliveries;
            public long poolReturns;
            public long reactiveChecksum;
            public long allocatedBytes;
            public long stopwatchFrequency;
            public string allocationScope = "Current-thread allocation during measured loop only; excludes setup, warmup, assertions, reporting and other threads.";
            public double totalMilliseconds;
            public double medianTickMilliseconds;
            public double p95TickMilliseconds;
            public double p99TickMilliseconds;
            public double maxTickMilliseconds;
            public int gc0Collections;
            public int gc1Collections;
            public int gc2Collections;
            public string unobserved = "GPU/process/native memory, thermal state, real frame pacing, cold start, long-duration resource retention.";
        }

        private sealed class Workload : IDisposable
        {
            public readonly TimerService Timers;
            public readonly ObjectPool<Item> Pool;
            public readonly Subject<int>[] Channels;
            private readonly IDisposable[] observers;
            private readonly int notifications;
            private readonly int poolOperationsPerSecond;
            private double poolRemainder;
            private int tick;
            public long TimerCallbacks;
            public long ReactiveDeliveries;
            public long ReactiveChecksum;
            public long PoolReturns;

            public Workload(int multiplier)
            {
                notifications = 20 * multiplier;
                poolOperationsPerSecond = 10 * multiplier;
                Timers = new TimerService(32 * multiplier);
                Action callback = () => TimerCallbacks++;
                for (int i = 0; i < Timers.Capacity; i++) Timers.Schedule(0, callback, interval: 0.01);
                Channels = new Subject<int>[16];
                observers = new IDisposable[16 * 4 * multiplier];
                int next = 0;
                for (int i = 0; i < Channels.Length; i++)
                {
                    Channels[i] = new Subject<int>();
                    for (int j = 0; j < 4 * multiplier; j++)
                        observers[next++] = Channels[i].Subscribe(value => { ReactiveDeliveries++; ReactiveChecksum += value; });
                }
                Pool = new ObjectPool<Item>(100 * multiplier, () => new Item(), item => item.Value = 0);
                var prewarm = new Item[Pool.Capacity];
                for (int i = 0; i < prewarm.Length; i++)
                {
                    if (!Pool.TryRent(out prewarm[i])) throw new InvalidOperationException("Pool prewarm exhausted declared capacity.");
                }
                for (int i = 0; i < prewarm.Length; i++) Pool.Return(prewarm[i]);
            }

            public void Tick(double delta)
            {
                Timers.Tick(delta, delta, Timers.Capacity);
                for (int i = 0; i < notifications; i++) Channels[(tick + i) % Channels.Length].OnNext(1);
                poolRemainder += poolOperationsPerSecond * delta;
                while (poolRemainder >= 1)
                {
                    if (!Pool.TryRent(out Item item)) throw new InvalidOperationException("Prewarmed pool failed to rent.");
                    item.Value = tick;
                    if (!Pool.Return(item)) throw new InvalidOperationException("Pool return failed.");
                    PoolReturns++;
                    poolRemainder -= 1;
                }
                tick++;
            }

            public void Dispose()
            {
                foreach (IDisposable observer in observers) observer.Dispose();
                foreach (Subject<int> channel in Channels) channel.Dispose();
                Timers.Dispose();
                Pool.Dispose();
            }
        }

        [TestCase(1)]
        [TestCase(4)]
        public void FixedCoreLoadProducesCorrectWorkAndRawObservations(int multiplier)
        {
            var report = new Observation
            {
                unityVersion = Application.unityVersion, operatingSystem = SystemInfo.operatingSystem, processor = SystemInfo.processorType,
                multiplier = multiplier, timerCount = 32 * multiplier, listenersPerChannel = 4 * multiplier,
                notificationsPerTick = 20 * multiplier, poolCapacity = 100 * multiplier, poolOperationsPerSecond = 10 * multiplier
            };
            var samples = new long[report.measuredTicks];
            var workload = new Workload(multiplier);
            try
            {
                for (int i = 0; i < report.warmupTicks; i++) workload.Tick(report.logicalDeltaSeconds);
                long callbacks = workload.TimerCallbacks, deliveries = workload.ReactiveDeliveries;
                long checksum = workload.ReactiveChecksum, returns = workload.PoolReturns;
                long frequency = Stopwatch.Frequency;
                report.stopwatchFrequency = frequency;
                Stopwatch.GetTimestamp();
                GC.GetAllocatedBytesForCurrentThread();
                int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long totalStart = Stopwatch.GetTimestamp();
                for (int i = 0; i < report.measuredTicks; i++)
                {
                    long start = Stopwatch.GetTimestamp();
                    workload.Tick(report.logicalDeltaSeconds);
                    samples[i] = Stopwatch.GetTimestamp() - start;
                }
                long totalEnd = Stopwatch.GetTimestamp();
                report.allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                report.gc0Collections = GC.CollectionCount(0) - gc0;
                report.gc1Collections = GC.CollectionCount(1) - gc1;
                report.gc2Collections = GC.CollectionCount(2) - gc2;
                report.totalMilliseconds = (totalEnd - totalStart) * 1000.0 / frequency;
                report.timerCallbacks = workload.TimerCallbacks - callbacks;
                report.reactiveDeliveries = workload.ReactiveDeliveries - deliveries;
                report.reactiveChecksum = workload.ReactiveChecksum - checksum;
                report.poolReturns = workload.PoolReturns - returns;
                Assert.AreEqual((long)report.measuredTicks * report.timerCount, report.timerCallbacks);
                Assert.AreEqual((long)report.measuredTicks * report.notificationsPerTick * report.listenersPerChannel, report.reactiveDeliveries);
                Assert.AreEqual(report.reactiveDeliveries, report.reactiveChecksum);
                Assert.That(report.poolReturns, Is.InRange((long)(report.measuredTicks * report.logicalDeltaSeconds * report.poolOperationsPerSecond) - 1,
                    (long)(report.measuredTicks * report.logicalDeltaSeconds * report.poolOperationsPerSecond) + 1));
                Assert.AreEqual(report.timerCount, workload.Timers.Count);
                Assert.AreEqual(report.poolCapacity, workload.Pool.Count);
                Assert.AreEqual(0, workload.Pool.RentedCount);
                string directory = "Builds/Reports/core-micro-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(directory);
                using (var writer = new StreamWriter(Path.Combine(directory, "samples.csv")))
                {
                    writer.WriteLine("tick,stopwatch_ticks");
                    for (int i = 0; i < samples.Length; i++) writer.WriteLine(i + "," + samples[i]);
                }
                Array.Sort(samples);
                report.medianTickMilliseconds = samples[(samples.Length - 1) / 2] * 1000.0 / frequency;
                report.p95TickMilliseconds = samples[(int)Math.Ceiling(samples.Length * 0.95) - 1] * 1000.0 / frequency;
                report.p99TickMilliseconds = samples[(int)Math.Ceiling(samples.Length * 0.99) - 1] * 1000.0 / frequency;
                report.maxTickMilliseconds = samples[samples.Length - 1] * 1000.0 / frequency;
                File.WriteAllText(Path.Combine(directory, "observations.json"), JsonUtility.ToJson(report, true));
            }
            finally { workload.Dispose(); }
            Assert.AreEqual(0, workload.Timers.Count);
            Assert.AreEqual(0, workload.Pool.Count);
        }
    }
}
