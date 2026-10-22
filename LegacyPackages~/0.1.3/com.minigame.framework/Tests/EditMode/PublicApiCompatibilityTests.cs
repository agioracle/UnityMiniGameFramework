using System;
using System.Linq;
using MiniGame.Framework.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MiniGame.Framework.Tests
{
    public sealed class PublicApiCompatibilityTests
    {
        public abstract class Fixture
        {
            protected Fixture() { }
            public const int Capacity = 12;
            public int Value { get; private set; }
            public abstract void Apply(ref int value, out string text);
            protected virtual T Create<T>(int count = 7, string label = "sample") where T : class, IDisposable, new() => new T();
            public void Append(params string[] values) { }
        }

        [Test]
        public void DetailedCaptureIncludesDefaultsConstraintsAndAccessors()
        {
            var snapshot = PublicApiCompatibility.CaptureTypes(new[] { typeof(Fixture) });
            Assert.AreEqual(2, snapshot.schemaVersion);
            Assert.IsTrue(snapshot.entries.Any(entry => entry.Contains("Create") && entry.Contains("default=System.Int32:7") && entry.Contains("System.IDisposable") && entry.Contains("flags=20")));
            Assert.IsTrue(snapshot.entries.Any(entry => entry.Contains("ctor protected")));
            Assert.IsTrue(snapshot.entries.Any(entry => entry.Contains("property Value") && entry.Contains("get=public") && entry.Contains("set=nonpublic")));
            Assert.IsTrue(snapshot.entries.Any(entry => entry.Contains("field Capacity") && entry.Contains("const=True value=System.Int32:12")));
            Assert.IsTrue(snapshot.entries.Any(entry => entry.Contains("Apply") && entry.Contains("out=True") && entry.Contains("System.Int32&")));
            Assert.IsTrue(snapshot.entries.Any(entry => entry.Contains("Append") && entry.Contains("System.ParamArrayAttribute")));
        }

        [Test]
        public void DefaultConstraintAndMemberRemovalProduceReviewDifferences()
        {
            var baseline = PublicApiCompatibility.CaptureTypes(new[] { typeof(Fixture) });
            var changed = new PublicApiCompatibility.Snapshot
            {
                entries = baseline.entries.Where(entry => !entry.Contains("field Capacity"))
                    .Select(entry => entry.Replace("default=System.Int32:7", "default=System.Int32:8").Replace("flags=20", "flags=4")).ToArray()
            };
            var difference = PublicApiCompatibility.Compare(baseline, changed);
            Assert.IsTrue(difference.requiresCompatibilityReview);
            Assert.IsTrue(difference.removed.Any(entry => entry.Contains("field Capacity")));
            Assert.IsTrue(difference.removed.Any(entry => entry.Contains("default=System.Int32:7")));
            Assert.IsTrue(difference.added.Any(entry => entry.Contains("default=System.Int32:8") && entry.Contains("flags=4")));
        }

        [Test]
        public void CaptureAndJsonRoundtripAreStableAndDefaultAssembliesContainNoBusiness()
        {
            var first = PublicApiCompatibility.Capture();
            var second = PublicApiCompatibility.Capture();
            CollectionAssert.AreEqual(first.entries, second.entries);
            var restored = JsonUtility.FromJson<PublicApiCompatibility.Snapshot>(JsonUtility.ToJson(first));
            var difference = PublicApiCompatibility.Compare(first, restored);
            Assert.IsFalse(difference.requiresCompatibilityReview);
            Assert.IsEmpty(difference.removed);
            Assert.IsEmpty(difference.added);
            Assert.IsTrue(first.entries.Any(entry => entry.Contains("MiniGame.Framework.ResourceService")));
            Assert.IsFalse(first.entries.Any(entry => entry.Contains("MiniGame.Business") || entry.Contains("MiniGame.Examples")));
            PublicApiCompatibility.ExportBaseline();
        }

        [Test]
        public void LegacyAndDuplicateOrEmptyInventoriesCannotMasqueradeAsDetailedBaselines()
        {
            var valid = PublicApiCompatibility.CaptureTypes(new[] { typeof(Fixture) });
            Assert.Throws<ArgumentException>(() => PublicApiCompatibility.Compare(new PublicApiCompatibility.Snapshot { schemaVersion = 1, entries = valid.entries }, valid));
            Assert.Throws<ArgumentException>(() => PublicApiCompatibility.Compare(new PublicApiCompatibility.Snapshot { entries = Array.Empty<string>() }, valid));
            Assert.Throws<ArgumentException>(() => PublicApiCompatibility.Compare(new PublicApiCompatibility.Snapshot { entries = new[] { "duplicate", "duplicate" } }, valid));
            Assert.Throws<ArgumentException>(() => PublicApiCompatibility.Compare(JsonUtility.FromJson<PublicApiCompatibility.Snapshot>("{\"publicApi\":[\"legacy\"]}"), valid));
        }
    }
}
