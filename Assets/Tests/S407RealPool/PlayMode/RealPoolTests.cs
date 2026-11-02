using System.Collections;
using Cysharp.Threading.Tasks;
using MiniFramework.S407Memory.Tests;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace MiniFramework.S407RealPool.Tests
{
    public sealed class RealPoolTests
    {
        [UnityTest] public IEnumerator BurstPreservesActiveLeaseAndRestoresPrefetch()=>Run(RealPoolScenario.Cases[0]);
        [UnityTest] public IEnumerator CloseWaitsForActualUnityDestruction()=>Run(RealPoolScenario.Cases[1]);
        [UnityTest] public IEnumerator CloseRetainsAssetsUntilReleaseAcknowledged()=>Run(RealPoolScenario.Cases[2]);
        static IEnumerator Run(string name)=>UniTask.ToCoroutine(async()=>
        {
            var inner=new MemoryApi();var api=new ObservedMemoryApi(inner);
            var result=await new RealPoolScenario(api).Run(name);
            Debug.Log("MINI_S407_REAL_POOL_CASE "+JsonUtility.ToJson(result));
            Assert.IsTrue(result.passed,JsonUtility.ToJson(result));
            Assert.AreEqual(1,api.OnCount);Assert.AreEqual(1,api.OffCount);Assert.IsEmpty(inner.Memory);
        });
    }
}
