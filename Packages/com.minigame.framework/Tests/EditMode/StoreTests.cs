using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace MiniGame.Framework.Tests
{
    public sealed class StoreTests
    {
        [Serializable]
        public sealed class Data { public int value; }
        private string directory;
        private LocalStore store;
        [SetUp]
        public void Setup()
        {
            directory = Path.Combine(UnityEngine.Application.temporaryCachePath, "minigame-tests-" + Guid.NewGuid().ToString("N"));
            store = new LocalStore(directory);
        }
        [TearDown]
        public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [Test]
        public void SaveRoundTripAndRecoverOlderValidSnapshot()
        {
            Assert.AreEqual(StoreStatus.Missing, store.Load<Data>("progress", 1).Status);
            Assert.IsTrue(store.Save("progress", 1, new Data { value = 1 }).Success);
            Assert.IsTrue(store.Save("progress", 1, new Data { value = 2 }).Success);
            Assert.AreEqual(2, store.Load<Data>("progress", 1).Value.value);
            File.WriteAllText(Path.Combine(directory, "progress.a.json"), "broken");
            Assert.AreEqual(1, store.Load<Data>("progress", 1).Value.value);
        }
        [Serializable]
        public sealed class TextData { public string s; }

        [Test]
        public void BoundaryPayloadDoesNotGainUnexpectedBomBytes()
        {
            var small = new LocalStore(directory, 128);
            var data = new TextData { s = new string('\"', 38) };
            Assert.IsTrue(small.Save("boundary", 1, data).Success);
            Assert.AreEqual(data.s, small.Load<TextData>("boundary", 1).Value.s);
        }

        [Test]
        public void NewSchemaCannotBeOverwrittenByOldClient()
        {
            store.Save("progress", 3, new Data { value = 3 });
            Assert.AreEqual(StoreStatus.NewerSchema, store.Load<Data>("progress", 1).Status);
            Assert.AreEqual(StoreStatus.NewerSchema, store.Save("progress", 1, new Data()).Status);
            Assert.AreEqual(3, store.Load<Data>("progress", 3).Value.value);
        }
        [Test]
        public void MigrationIsExplicitAndDoesNotDestroyOldSnapshot()
        {
            store.Save("progress", 1, new Data { value = 7 });
            Assert.AreEqual(StoreStatus.InvalidData, store.Load<Data>("progress", 2).Status);
            var result = store.Load<Data>("progress", 2, (json, version) => new Data { value = JsonCodec.Decode<Data>(json).value + version });
            Assert.AreEqual(8, result.Value.value);
            Assert.AreEqual(7, store.Load<Data>("progress", 1).Value.value);
        }
        [Test]
        public void AllCorruptDataIsNotAutomaticallyOverwritten()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "progress.a.json"), "broken");
            Assert.AreEqual(StoreStatus.Corrupt, store.Save("progress", 1, new Data()).Status);
            Assert.AreEqual("broken", File.ReadAllText(Path.Combine(directory, "progress.a.json")));
        }
        [Test]
        public void KeysCannotTraverseDirectories()
        {
            Assert.Throws<ArgumentException>(() => store.Save("../outside", 1, new Data()));
        }
        [Test]
        public void LocalizationCopiesDataAndFallsBack()
        {
            var text = new LocalizationService("en");
            var entries = new Dictionary<string, string> { { "hello", "Hello {0}" } };
            text.AddLanguage("en", entries);
            entries["hello"] = "changed";
            text.AddLanguage("fr", new Dictionary<string, string>());
            Assert.IsTrue(text.SetLanguage("fr"));
            Assert.AreEqual("Hello player", text.Format("hello", "player"));
            Assert.AreEqual("[missing]", text.Get("missing"));
            Assert.IsFalse(text.SetLanguage("unknown"));
        }
    }
}
