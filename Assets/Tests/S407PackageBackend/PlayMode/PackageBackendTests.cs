#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using YooAsset;
namespace MiniFramework.S407PackageBackend.Tests
{
    public sealed class PackageBackendTests
    {
        bool own;
        [SetUp] public void Before(){if(!File.Exists("s407-https-owned-build-profile.json"))Assert.Ignore("Owned real-bundle Profile required.");own=!YooAssets.IsInitialized;if(own)YooAssets.Initialize();}
        [TearDown] public void After(){if(own)YooAssets.Destroy();}
        IEnumerator Run(string name)=>UniTask.ToCoroutine(async()=>{
            var root=new Uri(Path.GetFullPath("Library/S407RealBundleEditorPackage")+"/").AbsoluteUri.TrimEnd('/');
            var scenario=new PackageBackendScenario(root,()=>{var fs=new FileSystemParameters("YooAsset.WebServerFileSystem",root);fs.AddParameter(EFileSystemParameter.DisableUnityWebCache,true);return new WebPlayModeOptions{WebServerFileSystemParameters=fs};});
            var r=await scenario.Run(name);Assert.IsTrue(r.passed,r.error);
        });
        [UnityTest] public IEnumerator ProductionBackendSharedLeases()=>Run(PackageBackendScenario.Names[0]);
        [UnityTest] public IEnumerator ProductionBackendMissingAsset()=>Run(PackageBackendScenario.Names[1]);
        [UnityTest] public IEnumerator ProductionBackendConfigMismatch()=>Run(PackageBackendScenario.Names[2]);
        [UnityTest] public IEnumerator ProductionBackendPayloadMismatch()=>Run(PackageBackendScenario.Names[3]);
        [UnityTest] public IEnumerator ProductionBackendForeignOwner()=>Run(PackageBackendScenario.Names[4]);
    }
}
#endif
