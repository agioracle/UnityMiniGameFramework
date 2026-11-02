using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat.Storage;
using MiniFramework.S407RestartRecovery;
using MiniFramework.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;
namespace MiniFramework.S407RestartRecovery.Tests
{
    public sealed class RestartRecoveryTests
    {
        sealed class Files : IWeChatStorageFiles
        {
            public readonly Dictionary<string, byte[]> Bytes = new Dictionary<string, byte[]>();
            public int Writes;
            public StoragePersistence Persistence => StoragePersistence.Volatile;
            public Result<StorageReadResult> Read(string key, int max) => Result<StorageReadResult>.Success(Bytes.TryGetValue(key, out var b) ? new StorageReadResult(true, b) : new StorageReadResult(false));
            public Result Write(string key, byte[] b) { Writes++; Bytes[key] = (byte[])b.Clone(); return Result.Success(); }
        }
        Files files; RestartRecoveryScenario scenario;
        async UniTask<RestartEvidence> Run(string launch = null)
        {
            scenario = new RestartRecoveryScenario(files, Guid.NewGuid().ToString("N"), launch ?? Guid.NewGuid().ToString("N"));
            await scenario.Execute(); return scenario.Evidence;
        }
        [UnityTearDown] public IEnumerator Cleanup() => UniTask.ToCoroutine(async () => { if (scenario != null) await scenario.Abort(); });
        [UnityTest] public IEnumerator SyntheticLaunchProtocolPreservesPendingThenRestoresExactlyOneGrant() => UniTask.ToCoroutine(async () => {
            files = new Files(); var a = await Run(); Assert.IsTrue(a.passed); Assert.AreEqual(1, a.checkpoint); Assert.AreEqual(0, a.coins); Assert.AreEqual(1, a.pendingClaims); Assert.AreEqual(1, a.injectedWriteFaults);
            var b = await Run(); Assert.IsTrue(b.passed && b.pendingWithoutProofPreserved && b.explicitAbandon); Assert.AreEqual(a.digest,b.previousDigest); Assert.AreEqual(10,b.completedCallbacks); Assert.AreEqual(5,b.coins);
            var c = await Run(); Assert.IsTrue(c.passed && c.chainComplete && c.duplicateRecoveryUnchanged); Assert.AreEqual(b.digest,c.previousDigest); Assert.AreEqual(3,c.generation); Assert.AreEqual(2,c.claimOwnerGeneration); Assert.AreEqual(0,c.adAttempts); Assert.AreEqual(5,c.coins);
            var d = await Run(); Assert.IsTrue(d.chainComplete); Assert.AreEqual(4,d.generation); Assert.AreEqual(5,d.coins); Assert.AreEqual(0,d.adAttempts);
        });
        [UnityTest] public IEnumerator SameLaunchCannotAdvanceCheckpoint() => UniTask.ToCoroutine(async () => {
            files = new Files(); await Run("synthetic-launch"); var writes=files.Writes;
            try { await Run("synthetic-launch"); Assert.Fail("must reject"); } catch(InvalidOperationException e) { Assert.AreEqual("checkpoint.fresh_runtime_required",e.Message); }
            Assert.AreEqual(writes,files.Writes);
        });
        [UnityTest] public IEnumerator CorruptMarkerPreservedWithoutWrites() => UniTask.ToCoroutine(async () => {
            files = new Files(); files.Bytes[RestartRecoveryScenario.MarkerKey]=System.Text.Encoding.UTF8.GetBytes("{}");
            try { await Run(); Assert.Fail("must reject"); } catch(InvalidOperationException e) { Assert.AreEqual("checkpoint.invalid",e.Message); }
            Assert.AreEqual(0,files.Writes);
        });
        [UnityTest] public IEnumerator MissingPriorFileCannotSilentlyReseed() => UniTask.ToCoroutine(async () => {
            files = new Files(); await Run(); var marker=files.Bytes[RestartRecoveryScenario.MarkerKey]; files.Bytes.Clear(); files.Bytes[RestartRecoveryScenario.MarkerKey]=marker; var writes=files.Writes;
            try { await Run(); Assert.Fail("must reject"); } catch(InvalidOperationException e) { Assert.AreEqual("checkpoint.file_missing",e.Message); }
            Assert.AreEqual(writes,files.Writes);
        });
    }
}
