using System;
using System.IO;
using System.Linq;
using MiniGame.Framework.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Framework.Tests
{
    public sealed class ResourceThresholdTests
    {
        private string directory;
        private string path;
        [SetUp]
        public void Setup()
        {
            directory = "Assets/ThresholdTest-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(directory);
            path = directory + "/ThresholdTexture.png";
            var texture = new Texture2D(16, 8, TextureFormat.RGBA32, false);
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
        [TearDown]
        public void Cleanup()
        {
            if (directory != null) AssetDatabase.DeleteAsset(directory);
        }
        [Test]
        public void ExactLimitsPassAndOverBudgetIsLocatedWithoutMutation()
        {
            byte[] previous = File.ReadAllBytes(path + ".meta");
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            int dimension = Math.Max(texture.width, texture.height);
            long bytes = new FileInfo(path).Length;
            var limits = new ResourceThresholdAudit.Limits { maxSourceBytes = bytes, maxTextureDimension = dimension };
            var exact = ResourceThresholdAudit.Inspect(new[] { path }, limits);
            Assert.AreEqual(0, exact.issues.Count);
            limits.maxSourceBytes = bytes - 1;
            limits.maxTextureDimension = dimension - 1;
            var failed = ResourceThresholdAudit.Inspect(new[] { path }, limits);
            Assert.IsTrue(failed.issues.Any(issue => issue.path == path && issue.property == "sourceBytes"));
            Assert.IsTrue(failed.issues.Any(issue => issue.path == path && issue.property == "textureDimensions"));
            CollectionAssert.AreEqual(previous, File.ReadAllBytes(path + ".meta"));
            Assert.AreEqual(bytes, new FileInfo(path).Length);
        }
        [Test]
        public void DependenciesAreDeduplicatedSortedAndLimitsAreCopied()
        {
            var limits = new ResourceThresholdAudit.Limits();
            var report = ResourceThresholdAudit.Inspect(new[] { path, path }, limits);
            Assert.AreEqual(1, report.assets.Count);
            limits.maxSourceBytes = 1;
            Assert.AreNotEqual(1, report.limits.maxSourceBytes);
            CollectionAssert.AreEqual(report.assets.Select(asset => asset.path).OrderBy(value => value, StringComparer.Ordinal), report.assets.Select(asset => asset.path));
        }
        [Test]
        public void UnknownSourceBytesAreExplicitNotZero()
        {
            var entry = new ResourceThresholdAudit.Entry();
            Assert.IsFalse(entry.sourceBytesKnown);
            Assert.AreEqual(-1, entry.sourceBytes);
            string json = JsonUtility.ToJson(entry);
            StringAssert.Contains("\"sourceBytes\":-1", json);
            StringAssert.Contains("\"sourceBytesKnown\":false", json);
        }

        [Test]
        public void InvalidInputsAndUnboundedBudgetsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => ResourceThresholdAudit.Inspect(new[] { path }, new ResourceThresholdAudit.Limits { maxAudioSeconds = double.NaN }));
            Assert.Throws<ArgumentException>(() => ResourceThresholdAudit.Inspect(new[] { path }, new ResourceThresholdAudit.Limits { maxSourceBytes = 0 }));
            Assert.Throws<ArgumentException>(() => ResourceThresholdAudit.Inspect(new[] { directory }, new ResourceThresholdAudit.Limits()));
            Assert.Throws<ArgumentException>(() => ResourceThresholdAudit.Inspect(new[] { "Assets/../outside" }, new ResourceThresholdAudit.Limits()));
        }
    }
}
