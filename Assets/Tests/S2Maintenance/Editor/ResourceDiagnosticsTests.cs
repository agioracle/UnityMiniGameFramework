using System.Linq;
using UnityEditor.Compilation;
using MiniFramework.Contracts;
using MiniFramework.EditorFoundation;
using NUnit.Framework;
using UnityEngine;
namespace MiniFramework.Maintenance.Tests
{
    public sealed class ResourceDiagnosticsTests
    {
        private sealed class Source:IAssetResourceDiagnostics
        {
            public int Reads;
            public AssetResourceSnapshot CaptureResources(){Reads++;return new AssetResourceSnapshot(new[]{new ResourceEntrySnapshot(new ResourceKey("fake","main","v1","icon",typeof(string)),1,false,false,false,true,true)},new[]{new ResourceOwnerSnapshot(new ResourceKey("fake","main","v1","icon",typeof(string)),"request-1","rent-42",ScopeState.Closing,true,true)});}
        }
        [Test] public void BindingPlayerAssemblyReferencesInstalledUiAndNeverEditor()
        {
            var assembly=CompilationPipeline.GetAssemblies(AssembliesType.Player).Single(a=>a.name=="MiniFramework.Bindings");
            var references=assembly.assemblyReferences.Select(a=>a.name).ToArray();
            CollectionAssert.Contains(references,"UnityEngine.UI");CollectionAssert.DoesNotContain(references,"Unity.ugui");
            Assert.IsFalse(references.Any(n=>n.Contains("Editor")));
        }
        [Test] public void SnapshotTextIncludesFrozenKeyOwnerAndFailure()
        {var source=new Source();var text=ResourceDiagnosticsWindow.Format(source.CaptureResources(),null);StringAssert.Contains("fake/main/v1/icon",text);StringAssert.Contains("owner=rent-42",text);StringAssert.Contains("failed=True",text);StringAssert.Contains("state=Closing",text);}
        [Test] public void EditorViewCapturesOnlyOnDemandAndDetachesSources()
        {var window=ScriptableObject.CreateInstance<ResourceDiagnosticsWindow>();try{var source=new Source();window.Attach(source,null);Assert.AreEqual(1,source.Reads);Assert.IsTrue(window.HasSources);window.Capture();Assert.AreEqual(2,source.Reads);window.Detach();Assert.IsFalse(window.HasSources);window.Capture();Assert.AreEqual(2,source.Reads);}finally{Object.DestroyImmediate(window);}}
    }
}
