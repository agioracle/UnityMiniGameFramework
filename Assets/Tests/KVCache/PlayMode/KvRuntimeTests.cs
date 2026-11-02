using System;
using System.Collections;
using MiniFramework.KVCache.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniFramework.KVCache.Tests
{
    public sealed class KvRuntimeTests
    {
        [UnityTest] public IEnumerator RuntimeStagesAcrossFramesAndExplicitFlushSurvivesNewWrapper()
        {
            var ns = "runtime-test-" + Guid.NewGuid().ToString("N");
            var physical = "miniframework.kv.v1:" + ns + ":level";
            try
            {
                var cache = UnityPlayerPrefsCache.Create(ns);
                Assert.IsTrue(cache.SetInt("level", 1).IsSuccess);
                yield return null;
                Assert.IsTrue(cache.SetInt("level", 8).IsSuccess);
                Assert.IsFalse(PlayerPrefs.HasKey(physical));
                Assert.AreEqual(8, cache.GetInt("level").Value);
                var flushed = cache.Flush(); Assert.IsTrue(flushed.IsSuccess); Assert.IsFalse(flushed.Value.PlatformPersistenceConfirmed);
                yield return null;
                var next = UnityPlayerPrefsCache.Create(ns);
                Assert.AreEqual(8, next.GetInt("level").Value);
                next.DeleteKey("level"); Assert.IsTrue(next.Flush().IsSuccess);
                Assert.IsFalse(UnityPlayerPrefsCache.Create(ns).HasKey("level").Value);
            }
            finally { PlayerPrefs.DeleteKey(physical); PlayerPrefs.Save(); }
        }
    }
}
