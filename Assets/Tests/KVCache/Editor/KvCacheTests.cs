using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using MiniFramework.Contracts;
using MiniFramework.KVCache;
using MiniFramework.KVCache.Unity;
using NUnit.Framework;
using UnityEngine;

namespace MiniFramework.KVCache.Tests
{
    public sealed class KvCacheTests
    {
        sealed class MemoryAccess : IPlayerPrefsAccess
        {
            public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
            public int Reads, Writes, Deletes, Saves, FailWriteAt;
            public bool FailRead, FailSave, FailDelete, ThrowAfterWrite;
            public Action OnAccess;
            public bool HasKey(string key) { Reads++; OnAccess?.Invoke(); if (FailRead) throw new Exception("private-value"); return Data.ContainsKey(key); }
            public string GetString(string key) { Reads++; return Data[key]; }
            public void SetString(string key, string value)
            { Writes++; OnAccess?.Invoke(); if (Writes == FailWriteAt) throw new Exception("private-value"); Data[key] = value; if (ThrowAfterWrite) throw new Exception(); }
            public void DeleteKey(string key) { Deletes++; if (FailDelete) throw new Exception(); Data.Remove(key); }
            public void Save() { Saves++; if (FailSave) throw new Exception("private-value"); }
        }
        MemoryAccess port;
        PlayerPrefsKvCache cache;
        const string Physical = "miniframework.kv.v1:settings:";
        [SetUp] public void SetUp() { port = new MemoryAccess(); cache = new PlayerPrefsKvCache("settings", port); }

        [Test] public void RepeatedSetUsesOnlyMemoryUntilExplicitFlush()
        {
            for (var i = 0; i < 100; i++) Assert.IsTrue(cache.SetInt("coins", i).IsSuccess);
            Assert.AreEqual(99, cache.GetInt("coins").Value);
            Assert.AreEqual(0, port.Reads + port.Writes + port.Saves);
            Assert.AreEqual(1, cache.PendingKeyCount);
            var receipt = cache.Flush().Value;
            Assert.AreEqual(1, port.Writes); Assert.AreEqual(1, port.Saves);
            Assert.AreEqual(1, receipt.AppliedKeyCount); Assert.IsTrue(receipt.SaveCalled);
            Assert.IsFalse(receipt.PlatformPersistenceConfirmed); Assert.IsFalse(cache.HasPendingChanges);
        }
        [Test] public void MissingDefaultsRemainCallSpecific()
        {
            Assert.AreEqual(7, cache.GetInt("absent", 7).Value);
            Assert.AreEqual(9, cache.GetInt("absent", 9).Value);
            Assert.AreEqual("later", cache.GetString("absent", "later").Value);
            Assert.IsFalse(cache.HasKey("absent").Value); Assert.AreEqual(1, port.Reads);
        }
        [Test] public void EmptyAndUnicodeStringsSurviveReopen()
        {
            cache.SetString("empty", ""); cache.SetString("unicode", "你好🙂\0kv1:i:2"); cache.Flush();
            var next = new PlayerPrefsKvCache("settings", port);
            Assert.IsTrue(next.HasKey("empty").Value); Assert.AreEqual("", next.GetString("empty", "fallback").Value);
            Assert.AreEqual("你好🙂\0kv1:i:2", next.GetString("unicode").Value);
        }
        [Test] public void TypesAndCultureRoundTrip()
        {
            var old = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                cache.SetInt("min", int.MinValue); cache.SetInt("max", int.MaxValue); cache.SetFloat("volume", 0.12345679f); cache.Flush();
                var next = new PlayerPrefsKvCache("settings", port);
                Assert.AreEqual(int.MinValue, next.GetInt("min").Value); Assert.AreEqual(int.MaxValue, next.GetInt("max").Value);
                Assert.AreEqual(0.12345679f, next.GetFloat("volume").Value);
            }
            finally { CultureInfo.CurrentCulture = old; }
        }
        [Test] public void WrongTypeIsVisibleBeforeAndAfterReopen()
        {
            cache.SetInt("typed", 1);
            Assert.AreEqual("kv.type_mismatch", cache.GetString("typed").Error.Code);
            cache.Flush(); Assert.AreEqual("kv.type_mismatch", new PlayerPrefsKvCache("settings", port).GetFloat("typed").Error.Code);
        }
        [TestCase("")] [TestCase("kv2:i:1")] [TestCase("kv1:q:data")] [TestCase("kv1:i:2147483648")] [TestCase("kv1:i:hello")]
        public void InvalidPersistedDataIsNotReplacedByDefault(string raw)
        {
            port.Data[Physical + "broken"] = raw;
            Assert.IsFalse(cache.GetInt("broken", 9).IsSuccess); Assert.IsTrue(cache.HasKey("broken").Value);
            Assert.AreEqual(raw, port.Data[Physical + "broken"]); Assert.IsFalse(cache.Flush().Value.SaveCalled);
        }
        [TestCase("kv1:f:NaN")] [TestCase("kv1:f:Infinity")] [TestCase("kv1:f:1e99")]
        public void NonfiniteStoredFloatIsInvalid(string raw)
        { port.Data[Physical + "number"] = raw; Assert.IsFalse(cache.GetFloat("number").IsSuccess); }
        [Test] public void NullReadForExistingKeyIsNotMissing()
        { port.Data[Physical + "broken"] = null; Assert.AreEqual("kv.value.invalid", cache.GetString("broken", "fallback").Error.Code); }
        [Test] public void ExplicitSetCanReplaceInvalidData()
        { port.Data[Physical + "broken"] = "old-format"; cache.GetInt("broken"); cache.SetInt("broken", 4); Assert.IsTrue(cache.Flush().IsSuccess); Assert.AreEqual(4, new PlayerPrefsKvCache("settings", port).GetInt("broken").Value); }
        [Test] public void NamespaceDeleteCannotRemoveOtherNamespaceOrLegacyKeys()
        {
            var other = new PlayerPrefsKvCache("other", port);
            port.Data["coins"] = "legacy"; other.SetInt("coins", 3); other.Flush(); cache.SetInt("coins", 5); cache.Flush();
            cache.DeleteKey("coins"); Assert.IsFalse(cache.HasKey("coins").Value); Assert.IsTrue(port.Data.ContainsKey(Physical + "coins")); cache.Flush();
            Assert.AreEqual(3, new PlayerPrefsKvCache("other", port).GetInt("coins").Value); Assert.AreEqual("legacy", port.Data["coins"]);
        }
        [Test] public void FinalMutationWinsWithinBatch()
        {
            cache.SetString("one", "a"); cache.DeleteKey("one"); cache.SetString("one", "b");
            cache.SetInt("two", 1); cache.DeleteKey("two");
            Assert.AreEqual(2, cache.Flush().Value.AppliedKeyCount); Assert.AreEqual(1, port.Writes); Assert.AreEqual(1, port.Deletes);
            Assert.AreEqual("b", new PlayerPrefsKvCache("settings", port).GetString("one").Value);
        }
        [Test] public void CleanSameValueAndEmptyFlushDoNotWriteAgain()
        { cache.SetInt("one", 1); cache.Flush(); cache.SetInt("one", 1); Assert.IsFalse(cache.Flush().Value.SaveCalled); Assert.AreEqual(1, port.Writes); Assert.AreEqual(1, port.Saves); }
        [Test] public void SaveFailureKeepsAllChangesForRetryWithoutClaimingRollback()
        {
            cache.SetInt("one", 7); port.FailSave = true;
            var result = cache.Flush(); Assert.AreEqual("kv.playerprefs.save_failed", result.Error.Code); Assert.AreEqual("", result.Error.Message);
            Assert.AreEqual("kv1:i:7", port.Data[Physical + "one"]); Assert.IsTrue(cache.HasPendingChanges);
            cache.InvalidateReadCache(); Assert.AreEqual(7, cache.GetInt("one").Value);
            port.FailSave = false; Assert.IsTrue(cache.Flush().IsSuccess); Assert.AreEqual(2, port.Writes); Assert.AreEqual(2, port.Saves);
        }
        [Test] public void PartialWriteFailureRetainsEntireBatch()
        {
            cache.SetInt("one", 1); cache.SetInt("two", 2); port.FailWriteAt = 2;
            Assert.AreEqual("kv.playerprefs.write_failed", cache.Flush().Error.Code);
            Assert.AreEqual(2, cache.PendingKeyCount); Assert.AreEqual(1, port.Data.Count); Assert.AreEqual(0, port.Saves);
            Assert.IsTrue(cache.Flush().IsSuccess); Assert.AreEqual(2, port.Data.Count); Assert.IsFalse(cache.HasPendingChanges);
        }
        [Test] public void ThrowAfterWriteDoesNotFabricateRollbackOrSuccessfulSave()
        {
            cache.SetInt("one", 4); port.ThrowAfterWrite = true;
            Assert.IsFalse(cache.Flush().IsSuccess); Assert.AreEqual("kv1:i:4", port.Data[Physical + "one"]);
            Assert.AreEqual(1, cache.PendingKeyCount); Assert.AreEqual(0, port.Saves);
            port.ThrowAfterWrite = false; Assert.IsTrue(cache.Flush().IsSuccess); Assert.AreEqual(1, port.Saves);
        }
        [Test] public void DeleteFailureRetainsTombstoneForRetry()
        {
            cache.SetInt("one", 1); cache.Flush(); cache.DeleteKey("one"); port.FailDelete = true;
            Assert.IsFalse(cache.Flush().IsSuccess); Assert.IsFalse(cache.HasKey("one").Value); Assert.AreEqual(1, cache.PendingKeyCount);
            port.FailDelete = false; Assert.IsTrue(cache.Flush().IsSuccess); Assert.IsFalse(port.Data.ContainsKey(Physical + "one"));
        }
        [Test] public void FailedReadIsRetriedAndNotCachedAsMissing()
        { port.FailRead = true; Assert.IsFalse(cache.GetInt("one").IsSuccess); Assert.AreEqual(0, cache.CachedKeyCount); port.FailRead = false; port.Data[Physical + "one"] = "kv1:i:4"; Assert.AreEqual(4, cache.GetInt("one").Value); }
        [Test] public void InvalidationRefreshesCleanValuesButPreservesPendingWrites()
        {
            port.Data[Physical + "one"] = "kv1:i:1"; Assert.AreEqual(1, cache.GetInt("one").Value);
            port.Data[Physical + "one"] = "kv1:i:2"; Assert.AreEqual(1, cache.GetInt("one").Value);
            cache.SetInt("pending", 8); cache.InvalidateReadCache(); Assert.AreEqual(2, cache.GetInt("one").Value); Assert.AreEqual(8, cache.GetInt("pending").Value);
        }
        [Test] public void BudgetRejectionPreservesExistingPendingValue()
        {
            var bounded = new PlayerPrefsKvCache("settings", port, new KvCacheOptions(1, 100));
            bounded.SetString("one", "ok"); Assert.AreEqual("kv.cache.budget", bounded.SetInt("two", 2).Error.Code);
            Assert.AreEqual("kv.cache.budget", bounded.SetString("one", new string('x', 101)).Error.Code);
            Assert.AreEqual("ok", bounded.GetString("one").Value); Assert.IsTrue(bounded.Flush().IsSuccess);
            bounded.InvalidateReadCache(); Assert.IsTrue(bounded.SetInt("two", 2).IsSuccess);
        }
        [Test] public void Utf8BudgetCountsNonAsciiAndEncodedKey()
        {
            var bytes = System.Text.Encoding.UTF8.GetByteCount(Physical + "one" + "kv1:s:你好");
            Assert.IsTrue(new PlayerPrefsKvCache("settings", port, new KvCacheOptions(1, bytes)).SetString("one", "你好").IsSuccess);
            Assert.IsFalse(new PlayerPrefsKvCache("settings", port, new KvCacheOptions(1, bytes - 1)).SetString("one", "你好").IsSuccess);
        }
        [Test] public void ReentrantBackendCannotMutateOrFlushCache()
        {
            Result nested = default; Result<KvFlushReceipt> flushed = default;
            port.OnAccess = () => { nested = cache.SetInt("nested", 2); flushed = cache.Flush(); };
            cache.SetInt("one", 1); Assert.IsTrue(cache.Flush().IsSuccess);
            Assert.AreEqual("kv.busy", nested.Error.Code); Assert.AreEqual("kv.busy", flushed.Error.Code); Assert.AreEqual(1, port.Data.Count);
        }
        [Test] public void WorkerDoesNotCallBackendOrModifyCache()
        {
            Result<int> read = default; Result written = default; Result<KvFlushReceipt> flushed = default;
            var thread = new Thread(() => { read = cache.GetInt("one"); written = cache.SetInt("one", 2); flushed = cache.Flush(); });
            thread.Start(); thread.Join(); Assert.AreEqual("kv.thread", read.Error.Code); Assert.AreEqual("kv.thread", written.Error.Code); Assert.AreEqual("kv.thread", flushed.Error.Code);
            Assert.AreEqual(0, port.Reads + port.Writes + port.Saves); Assert.AreEqual(0, cache.CachedKeyCount);
        }
        [Test] public void InvalidInputsCannotReachBackend()
        {
            Assert.IsFalse(cache.SetInt("../bad", 1).IsSuccess); Assert.IsFalse(cache.GetInt(null).IsSuccess);
            Assert.IsFalse(cache.SetString("one", null).IsSuccess); Assert.IsFalse(cache.SetFloat("one", float.NaN).IsSuccess); Assert.IsFalse(cache.SetFloat("one", float.PositiveInfinity).IsSuccess);
            Assert.Throws<ArgumentException>(() => new PlayerPrefsKvCache("a:b", port)); Assert.Throws<ArgumentOutOfRangeException>(() => new KvCacheOptions(0));
            Assert.AreEqual(0, port.Reads + port.Writes + port.Saves);
        }
    }

    public sealed class UnityPlayerPrefsTests
    {
        string ns;
        string Key(string key) => "miniframework.kv.v1:" + ns + ":" + key;
        [SetUp] public void SetUp() { ns = "test-" + Guid.NewGuid().ToString("N"); }
        [TearDown] public void TearDown()
        {
            // Delete only the test's known, unique keys. Never clear user preferences.
            foreach (var key in new[] { "integer", "fraction", "text", "empty" }) PlayerPrefs.DeleteKey(Key(key));
            PlayerPrefs.Save();
        }
        [Test] public void RealPlayerPrefsFlushAndNewWrapperReadAllTypes()
        {
            IKvCache first = UnityPlayerPrefsCache.Create(ns);
            Assert.IsTrue(first.SetInt("integer", 42).IsSuccess); Assert.IsTrue(first.SetFloat("fraction", 0.75f).IsSuccess);
            Assert.IsTrue(first.SetString("text", "中文🙂").IsSuccess); Assert.IsTrue(first.SetString("empty", "").IsSuccess);
            Assert.IsFalse(PlayerPrefs.HasKey(Key("integer")));
            var flush = first.Flush(); Assert.IsTrue(flush.IsSuccess); Assert.IsTrue(flush.Value.SaveCalled); Assert.IsFalse(flush.Value.PlatformPersistenceConfirmed);
            IKvCache second = UnityPlayerPrefsCache.Create(ns);
            Assert.AreEqual(42, second.GetInt("integer").Value); Assert.AreEqual(0.75f, second.GetFloat("fraction").Value);
            Assert.AreEqual("中文🙂", second.GetString("text").Value); Assert.IsTrue(second.HasKey("empty").Value); Assert.AreEqual("", second.GetString("empty", "default").Value);
        }
        [Test] public void RealDeleteIsDeferredAndOtherKeysSurvive()
        {
            var cache = UnityPlayerPrefsCache.Create(ns); cache.SetInt("integer", 8); cache.SetString("text", "keep"); cache.Flush();
            cache.DeleteKey("integer"); Assert.IsTrue(PlayerPrefs.HasKey(Key("integer"))); Assert.IsFalse(cache.HasKey("integer").Value);
            Assert.IsTrue(cache.Flush().IsSuccess); Assert.IsFalse(PlayerPrefs.HasKey(Key("integer"))); Assert.IsTrue(PlayerPrefs.HasKey(Key("text")));
        }
        [Test] public void RealUnknownFormatIsPreservedUntilExplicitReplacement()
        {
            PlayerPrefs.SetString(Key("text"), "kv99:s:future"); PlayerPrefs.Save();
            var cache = UnityPlayerPrefsCache.Create(ns); Assert.AreEqual("kv.value.invalid", cache.GetString("text").Error.Code);
            Assert.IsFalse(cache.Flush().Value.SaveCalled); Assert.AreEqual("kv99:s:future", PlayerPrefs.GetString(Key("text")));
        }
    }
}
