using System;
using System.IO;
using System.Linq;
using MiniGame.Framework.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Framework.Tests
{
    public sealed class ResourceMediaThresholdTests
    {
        private string directory;
        [SetUp]
        public void Setup()
        {
            directory = "Assets/MediaThreshold-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(directory);
        }
        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(directory);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void ImportedAudioDurationUsesMeasuredLengthAndExactLimitPasses()
        {
            string path = directory + "/Tone.wav";
            const int sampleRate = 8000;
            using (var stream = new FileStream(path, FileMode.CreateNew))
            using (var writer = new BinaryWriter(stream))
            {
                int dataBytes = sampleRate * 2;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(sampleRate); writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(dataBytes);
                for (int i = 0; i < sampleRate; i++) writer.Write((short)0);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var audio = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            Assert.IsTrue(audio);
            byte[] meta = File.ReadAllBytes(path + ".meta");
            var limits = new ResourceThresholdAudit.Limits { maxAudioSeconds = audio.length };
            var exact = ResourceThresholdAudit.Inspect(new[] { path }, limits);
            Assert.IsFalse(exact.issues.Any(issue => issue.property == "audioSeconds"));
            Assert.AreEqual(audio.length, exact.assets.Single(entry => entry.path == path).audioSeconds);
            limits.maxAudioSeconds = audio.length / 2;
            var over = ResourceThresholdAudit.Inspect(new[] { path }, limits);
            Assert.IsTrue(over.issues.Any(issue => issue.path == path && issue.property == "audioSeconds"));
            CollectionAssert.AreEqual(meta, File.ReadAllBytes(path + ".meta"));
        }

        [Test]
        public void SharedShaderDependencyCountsOnceAndFlagsExceededBudget()
        {
            string shaderPath = directory + "/Audit.shader";
            File.WriteAllText(shaderPath, "Shader \"Hidden/MiniGame/Threshold/" + Guid.NewGuid().ToString("N") + "\" { SubShader { Pass {} } }");
            AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            Assert.IsTrue(shader);
            string firstPath = directory + "/First.mat", secondPath = directory + "/Second.mat";
            AssetDatabase.CreateAsset(new Material(shader), firstPath);
            AssetDatabase.CreateAsset(new Material(shader), secondPath);
            AssetDatabase.SaveAssets();
            var limits = new ResourceThresholdAudit.Limits { maxShaderCount = 1 };
            var report = ResourceThresholdAudit.Inspect(new[] { firstPath, secondPath, shaderPath }, limits);
            Assert.AreEqual(1, report.shaderCount);
            Assert.AreEqual(1, report.assets.Count(entry => entry.path == shaderPath));
            Assert.IsFalse(report.issues.Any(issue => issue.property == "shaderCount"));
            limits.maxShaderCount = 0;
            report = ResourceThresholdAudit.Inspect(new[] { firstPath, secondPath }, limits);
            Assert.IsTrue(report.issues.Any(issue => issue.property == "shaderCount"));
        }

        [Test]
        public void EmptyFontAndOversizedAtlasHaveSeparateFindings()
        {
            string fontPath = directory + "/EmptyFont.asset";
            var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            font.atlasTextures = new[] { new Texture2D(32, 16, TextureFormat.Alpha8, false) };
            AssetDatabase.CreateAsset(font, fontPath);
            AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
            AssetDatabase.SaveAssets();
            var report = ResourceThresholdAudit.Inspect(new[] { fontPath }, new ResourceThresholdAudit.Limits { maxTextureDimension = 16 });
            Assert.IsTrue(report.issues.Any(issue => issue.path == fontPath && issue.property == "fontCharacters"));
            Assert.IsTrue(report.issues.Any(issue => issue.path == fontPath && issue.property == "textureDimensions"));
            Assert.IsTrue(report.assets.Single(entry => entry.path == fontPath).sourceBytesKnown);
        }
    }
}
