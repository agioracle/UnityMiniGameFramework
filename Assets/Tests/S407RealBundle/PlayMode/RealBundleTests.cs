#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using YooAsset;
namespace MiniFramework.S407RealBundle.Tests
{
    public sealed class RealBundleTests
    {
        bool ownSdk;
        [SetUp] public void SetUp() { ownSdk = false; if (!File.Exists("s407-https-owned-build-profile.json")) Assert.Ignore("Owned real-bundle build profile required; no main-project bundle generation."); ownSdk = !YooAssets.IsInitialized; if (ownSdk) YooAssets.Initialize(); }
        [TearDown] public void TearDown() { if (ownSdk) YooAssets.Destroy(); }
        static InitializePackageOptions Options()
        {
            var root = Path.GetFullPath("Library/S407RealBundleEditorPackage");
            Assert.IsTrue(Directory.Exists(root), "Run owned bundle build first; EditorSimulate is intentionally excluded.");
            var fs = new FileSystemParameters("YooAsset.WebServerFileSystem", new Uri(root + "/").AbsoluteUri.TrimEnd('/'));
            fs.AddParameter(EFileSystemParameter.DisableUnityWebCache, true);
            return new WebPlayModeOptions { WebServerFileSystemParameters = fs };
        }
        IEnumerator Run(string name) => UniTask.ToCoroutine(async () => { var r = await new RealBundleScenario(Options).RunCase(name); Assert.IsTrue(r.passed); });
        [UnityTest] public IEnumerator ActualBundleTwentyLeasesAndUnload() => Run("ready_shared_leases");
        [UnityTest] public IEnumerator MissingFirstPlayPreventsConsumer() => Run("missing_first_play");
        [UnityTest] public IEnumerator ConfigMismatchPreventsConsumerAndUnloads() => Run("config_mismatch");
        [UnityTest] public IEnumerator ActualPayloadMismatchPreventsReady() => Run("payload_mismatch");
    }
}
#endif
