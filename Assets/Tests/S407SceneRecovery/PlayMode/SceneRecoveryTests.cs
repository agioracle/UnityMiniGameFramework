#if UNITY_EDITOR
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
namespace MiniFramework.S407SceneRecovery.Tests
{
    public sealed class SceneRecoveryTests
    {
        IEnumerator Run(int index) => UniTask.ToCoroutine(async () => {
            var row = await new SceneRecoveryScenario().Run(SceneRecoveryScenario.Names[index]);
            Assert.IsTrue(row.passed, row.error);
            Assert.AreEqual(index < 2 ? 1 : 2, row.nativeLoads);
            Assert.AreEqual(row.nativeLoads, row.nativeActivations, "Actual fixture OnEnable observations");
        });
        [UnityTest] public IEnumerator DeferredOwnerCloseActivatesThenUnloadsActualScene() => Run(0);
        [UnityTest] public IEnumerator CanceledFlowDrainsNativeLoadAndPreservesOldSession() => Run(1);
        [UnityTest] public IEnumerator FailedOldUnloadPreservesNewAndExplicitRetryDrains() => Run(2);
        [UnityTest] public IEnumerator RepeatedPathUsesDistinctNativeHandles() => Run(3);
    }
}
#endif
