using System;
using System.IO;
using System.Linq;
using MiniGame.Framework.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Framework.Tests
{
    public sealed class AssetImportConfigurationTests
    {
        private string directory;

        [SetUp]
        public void Setup()
        {
            directory = "Assets/ImportAudit-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(directory);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        private string TextureFixture()
        {
            string path = directory + "/Sprite.png";
            var texture = new Texture2D(32, 16, TextureFormat.RGBA32, false);
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.isReadable = true;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return path;
        }

        [Test]
        public void ReadableMippedUncompressedSpriteIsLocatedWithoutChangingImporter()
        {
            string path = TextureFixture();
            byte[] originalMeta = File.ReadAllBytes(path + ".meta");
            var report = AssetImportConfigurationAudit.Inspect(new[] { path });
            var entry = report.assets.Single();
            Assert.AreEqual(path, entry.path);
            Assert.AreEqual(32, entry.sourceWidth);
            Assert.AreEqual(16, entry.sourceHeight);
            Assert.IsTrue(entry.textureReadable && entry.mipmapsEnabled);
            Assert.AreEqual("Uncompressed", entry.defaultTextureCompression);
            CollectionAssert.AreEquivalent(new[] { "textureReadable", "spriteMipmaps", "textureCompression" }, report.findings.Select(item => item.property));
            CollectionAssert.AreEqual(originalMeta, File.ReadAllBytes(path + ".meta"));
            StringAssert.Contains("review prompts", report.scope);
        }

        [Test]
        public void DuplicateInputsHaveStableOneEntryAndNoAutomaticRepair()
        {
            string path = TextureFixture();
            var first = AssetImportConfigurationAudit.Inspect(new[] { path, path });
            var second = AssetImportConfigurationAudit.Inspect(new[] { path });
            Assert.AreEqual(1, first.assets.Count);
            Assert.AreEqual(JsonUtility.ToJson(first), JsonUtility.ToJson(second));
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.IsTrue(importer.isReadable && importer.mipmapEnabled);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
        }

        [Test]
        public void AudioDefaultLoadAndPreloadSettingsAreReportedWithoutMutation()
        {
            string path = directory + "/Tone.wav";
            using (var writer = new BinaryWriter(new FileStream(path, FileMode.CreateNew)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 1600);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1);
                writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(1600);
                for (int i = 0; i < 800; i++) writer.Write((short)0);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var defaults = importer.defaultSampleSettings;
            defaults.loadType = AudioClipLoadType.DecompressOnLoad;
            defaults.compressionFormat = AudioCompressionFormat.PCM;
            defaults.preloadAudioData = true;
            importer.defaultSampleSettings = defaults;
            importer.loadInBackground = true;
            importer.SaveAndReimport();
            byte[] originalMeta = File.ReadAllBytes(path + ".meta");
            var report = AssetImportConfigurationAudit.Inspect(new[] { path });
            var entry = report.assets.Single();
            Assert.AreEqual("DecompressOnLoad", entry.defaultAudioLoadType);
            Assert.AreEqual("PCM", entry.defaultAudioCompression);
            Assert.IsTrue(entry.preloadAudioData && entry.loadAudioInBackground);
            CollectionAssert.AreEquivalent(new[] { "audioPreload", "audioCompression" }, report.findings.Select(item => item.property));
            CollectionAssert.AreEqual(originalMeta, File.ReadAllBytes(path + ".meta"));
        }

        [Test]
        public void EmptyFolderAndTraversalInputsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => AssetImportConfigurationAudit.Inspect(Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => AssetImportConfigurationAudit.Inspect(new[] { directory }));
            Assert.Throws<ArgumentException>(() => AssetImportConfigurationAudit.Inspect(new[] { "Assets/../outside.png" }));
        }
    }
}
