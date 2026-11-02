#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using YooAsset;
namespace MiniFramework.S407PackageRecovery.Tests
{
    public sealed class PackageRecoveryTests
    {
        bool own;
        [SetUp] public void Before()
        {
            if (!File.Exists("s407-https-owned-build-profile.json")) Assert.Ignore("Owned real-bundle Profile required.");
            own = !YooAssets.IsInitialized; if (own) YooAssets.Initialize();
        }
        [TearDown] public void After() { if (own) YooAssets.Destroy(); }
        IEnumerator Run(string name) => UniTask.ToCoroutine(async () => {
            if (name == PackageRecoveryScenario.Names[0] || name == PackageRecoveryScenario.Names[2])
                LogAssert.Expect(LogType.Error, new Regex("^\\[UnityWebRequestBytes\\] Request failed\\..*__s407_missing_candidate__/BuiltinCatalog\\.bytes"));
            var root = new Uri(Path.GetFullPath("Library/S407RealBundleEditorPackage") + "/").AbsoluteUri.TrimEnd('/');
            var scenario = new PackageRecoveryScenario(root, location => {
                var fs = new FileSystemParameters("YooAsset.WebServerFileSystem", location); fs.AddParameter(EFileSystemParameter.DisableUnityWebCache, true);
                return new WebPlayModeOptions { WebServerFileSystemParameters = fs };
            });
            var result = await scenario.Run(name); Assert.IsTrue(result.passed, result.error);
        });
        [UnityTest] public IEnumerator InitializationFailureCleansBeforeFallback() => Run(PackageRecoveryScenario.Names[0]);
        [UnityTest] public IEnumerator ManifestFailureCleansBeforeFallback() => Run(PackageRecoveryScenario.Names[1]);
        [UnityTest] public IEnumerator AllFailedDrainsBeforeNewContextRecovery() => Run(PackageRecoveryScenario.Names[2]);
        [UnityTest] public IEnumerator CloseWaitsForActualPreparationAcknowledgement() => Run(PackageRecoveryScenario.Names[3]);
    }
}
#endif
