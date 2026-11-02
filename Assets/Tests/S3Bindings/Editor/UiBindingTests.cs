using System;
using System.IO;
using System.Linq;
using Game.Features;
using MiniFramework.UI;
using MiniFramework.UI.Editor;
using MiniFramework.EditorFoundation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Object=UnityEngine.Object;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Cysharp.Threading.Tasks;
namespace MiniFramework.UIBindings.Tests
{
    public sealed class UiBindingTests
    {
        const string Folder="Assets/__S302Test";
        GameObject root,prefab;UiBindingManifest manifest;string temporary;
        [SetUp] public void SetUp()
        {
            Assert.IsFalse(AssetDatabase.IsValidFolder(Folder),"Reserved fixture folder already exists; do not overwrite user data.");
            AssetDatabase.CreateFolder("Assets","__S302Test");
            root=new GameObject("Panel",typeof(RectTransform),typeof(CanvasGroup),typeof(BindingDemoView));
            new GameObject("Content",typeof(RectTransform)).transform.SetParent(root.transform,false);
            new GameObject("Action",typeof(RectTransform),typeof(Button)).transform.SetParent(root.transform,false);
            prefab=PrefabUtility.SaveAsPrefabAsset(root,Folder+"/Panel.prefab");
            manifest=ScriptableObject.CreateInstance<UiBindingManifest>();manifest.Prefab=prefab;
            manifest.PanelScript=AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Game/Features/BindingDemoView.cs");
            manifest.Fields=new[]{new UiBindingManifest.Entry{Name="ActionButton",Target=prefab.transform.Find("Action").GetComponent<Button>()},new UiBindingManifest.Entry{Name="Group",Target=prefab.GetComponent<CanvasGroup>()},new UiBindingManifest.Entry{Name="Content",Target=prefab.transform.Find("Content").GetComponent<RectTransform>()}};
            temporary=Path.Combine(Path.GetTempPath(),"mini-s302-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
        }
        [TearDown] public void TearDown(){Object.DestroyImmediate(root);Object.DestroyImmediate(manifest);AssetDatabase.DeleteAsset(Folder);Directory.Delete(temporary,true);}
        [Test] public void NamespaceValidationRejectsKeywordsAndEmptySegments(){Assert.IsTrue(GeneratedSource.Namespace("Game.Features"));Assert.IsFalse(GeneratedSource.Namespace("Game.class"));Assert.IsFalse(GeneratedSource.Namespace("Game..UI"));Assert.IsFalse(GeneratedSource.Namespace(""));}
        [Test] public void IdentifierRejectsInjectionAndKeywords(){foreach(var s in new[]{"class","x;","a-b","1abc","x\n","@class",""})Assert.IsFalse(GeneratedSource.Identifier(s),s);}
        [Test] public void RenderMatchesCompiledGeneratedArtifact(){Assert.AreEqual(File.ReadAllText(UiBindingGenerator.OutputPath(manifest)),UiBindingGenerator.Render(manifest));UiBindingGenerator.ValidateCompiled(manifest);}
        [Test] public void RepeatedGenerationPreservesSourceAndHandwrittenTimestamp(){var path=UiBindingGenerator.OutputPath(manifest);var time=File.GetLastWriteTimeUtc(path);var hand=File.ReadAllText("Assets/Game/Features/BindingDemoView.cs");Assert.IsFalse(UiBindingGenerator.Generate(manifest));Assert.AreEqual(time,File.GetLastWriteTimeUtc(path));Assert.AreEqual(hand,File.ReadAllText("Assets/Game/Features/BindingDemoView.cs"));}
        [Test] public void FieldOrderDoesNotChangeOutput(){var a=UiBindingGenerator.Render(manifest);Array.Reverse(manifest.Fields);Assert.AreEqual(a,UiBindingGenerator.Render(manifest));}
        [Test] public void DuplicateFieldRejected(){manifest.Fields[0].Name="Content";Assert.Throws<InvalidOperationException>(()=>UiBindingGenerator.Render(manifest));}
        [Test] public void KeywordFieldRejected(){manifest.Fields[0].Name="class";Assert.Throws<InvalidOperationException>(()=>UiBindingGenerator.Render(manifest));}
        [Test] public void InheritedMemberConflictRejected(){manifest.Fields[0].Name="transform";Assert.Throws<InvalidOperationException>(()=>UiBindingGenerator.Render(manifest));}
        [Test] public void LifecycleMethodConflictRejected(){manifest.Fields[0].Name="OnOpenAsync";Assert.Throws<InvalidOperationException>(()=>UiBindingGenerator.Render(manifest));}
        [Test] public void NullTargetRejected(){manifest.Fields[0].Target=null;Assert.Throws<InvalidOperationException>(()=>UiBindingGenerator.Render(manifest));}
        [Test] public void OutsideRootTargetRejected(){manifest.Fields[0].Target=root.GetComponent<CanvasGroup>();Assert.Throws<InvalidOperationException>(()=>UiBindingGenerator.Render(manifest));}
        [Test] public void MissingScriptRejected(){manifest.PanelScript=null;Assert.Throws<InvalidOperationException>(()=>UiBindingGenerator.Render(manifest));}
        [Test] public void NonPartialPanelRejected(){manifest.PanelScript=AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Game/Features/HomeView.cs");Assert.Throws<InvalidOperationException>(()=>UiBindingGenerator.Render(manifest));}
        [Test] public void SceneObjectCannotBeGenerationPrefab(){manifest.Prefab=root;Assert.Throws<InvalidOperationException>(()=>UiBindingGenerator.Render(manifest));}
        [Test] public void MissingPathIsLocated(){var e=Assert.Throws<InvalidOperationException>(()=>UiPrefabBindings.Resolve(root.transform,"Missing",typeof(RectTransform)));StringAssert.Contains("Missing",e.Message);}
        [Test] public void DuplicateSiblingPathRejected(){var duplicate=new GameObject("Content",typeof(RectTransform));duplicate.transform.SetParent(root.transform,false);Assert.Throws<InvalidOperationException>(()=>UiPrefabBindings.BindRoot(root));}
        [Test] public void MissingComponentRejected(){Assert.Throws<InvalidOperationException>(()=>UiPrefabBindings.Resolve(root.transform,"Content",typeof(CanvasGroup)));}
        [Test] public void InactiveChildrenAreBound(){root.transform.Find("Content").gameObject.SetActive(false);Assert.AreEqual(3,UiPrefabBindings.BindRoot(root));Assert.IsNotNull(root.GetComponent<BindingDemoView>().Content);}
        [Test] public void BindIsIdempotent(){Assert.AreEqual(3,UiPrefabBindings.BindRoot(root));Assert.AreEqual(0,UiPrefabBindings.BindRoot(root));UiPrefabBindings.ValidateRoot(root);}
        [Test] public void BindValidationIsAtomic(){root.transform.Find("Content").name="Other";Assert.Throws<InvalidOperationException>(()=>UiPrefabBindings.BindRoot(root));Assert.IsNull(root.GetComponent<BindingDemoView>().Group);}
        [Test] public void UndoAndRedoRestoreBinding(){UiPrefabBindings.BindRoot(root);Undo.FlushUndoRecordObjects();Assert.IsNotNull(root.GetComponent<BindingDemoView>().Content);Undo.PerformUndo();Assert.IsNull(root.GetComponent<BindingDemoView>().Content);Undo.PerformRedo();Assert.IsNotNull(root.GetComponent<BindingDemoView>().Content);}
        [Test] public void PrefabSavedReferencesAndGuidSurviveReload(){var path=AssetDatabase.GetAssetPath(prefab);var guid=AssetDatabase.AssetPathToGUID(path);Assert.AreEqual(3,UiPrefabBindings.BindPrefab(prefab));AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);var reloaded=AssetDatabase.LoadAssetAtPath<GameObject>(path);UiPrefabBindings.ValidateRoot(reloaded);Assert.AreEqual(guid,AssetDatabase.AssetPathToGUID(path));Assert.AreEqual(0,UiPrefabBindings.BindPrefab(reloaded));}
        [Test] public void BrokenReferenceFailsRuntimeValidation(){Assert.IsFalse(root.GetComponent<BindingDemoView>().ValidateGeneratedBindings().IsSuccess);UiPrefabBindings.BindRoot(root);Assert.IsTrue(root.GetComponent<BindingDemoView>().ValidateGeneratedBindings().IsSuccess);Object.DestroyImmediate(root.transform.Find("Content").gameObject);Assert.IsFalse(root.GetComponent<BindingDemoView>().ValidateGeneratedBindings().IsSuccess);}
        [Test] public void BuildCheckRejectsUnboundPrefabWithPath(){var e=Assert.Throws<BuildFailedException>(()=>UiBindingBuildCheck.ValidateAllPrefabs());StringAssert.Contains("Panel.prefab",e.Message);}
        [Test] public void BuildCheckAcceptsBoundPrefab(){UiPrefabBindings.BindPrefab(prefab);Assert.DoesNotThrow(()=>UiBindingBuildCheck.ValidateAllPrefabs());}
        [Test] public void ForeignFileIsNeverOverwritten(){var path=Path.Combine(temporary,"file");File.WriteAllText(path,"handwritten");Assert.Throws<InvalidOperationException>(()=>GeneratedSource.Write(path,"new","// owned"));Assert.AreEqual("handwritten",File.ReadAllText(path));}
        [Test] public void EditedGeneratedFileIsNeverOverwritten(){var path=Path.Combine(temporary,"file");GeneratedSource.Write(path,"// owned\none","// owned");File.AppendAllText(path,"edited");Assert.Throws<InvalidOperationException>(()=>GeneratedSource.Write(path,"// owned\ntwo","// owned"));}
        [Test] public void OwnedUnmodifiedFileCanRegenerate(){var path=Path.Combine(temporary,"file");Assert.IsTrue(GeneratedSource.Write(path,"// owned\none","// owned"));Assert.IsTrue(GeneratedSource.Write(path,"// owned\ntwo","// owned"));Assert.IsFalse(GeneratedSource.Write(path,"// owned\ntwo","// owned"));Assert.AreEqual(GeneratedSource.Hash(File.ReadAllText(path)),File.ReadAllText(path+".sha256"));}
        [Test] public void PersistentPrefabRequiresExplicitSavePath(){Assert.Throws<InvalidOperationException>(()=>UiPrefabBindings.BindRoot(prefab));}
        [Test] public void DuplicateComponentRejected(){root.AddComponent<BoxCollider>();root.AddComponent<BoxCollider>();Assert.AreEqual(2,root.GetComponents<BoxCollider>().Length);Assert.Throws<InvalidOperationException>(()=>UiPrefabBindings.Resolve(root.transform,"",typeof(BoxCollider)));}
        [UnityTest] public System.Collections.IEnumerator GeneratedButtonSubscriptionBelongsToViewScope()=>UniTask.ToCoroutine(async()=>
        {
            UiPrefabBindings.BindRoot(root);var panel=root.GetComponent<BindingDemoView>();
            for(int i=0;i<100;i++)
            {
                var view=new Scope("generated-view-"+i,ScopeKind.View);
                Assert.IsTrue((await panel.OnOpenAsync(view,OperationContext.Unlimited())).IsSuccess);
                panel.ActionButton.onClick.Invoke();Assert.AreEqual(i+1,panel.ClickCount);
                Assert.IsTrue((await view.CloseAsync(new CleanupContext(default,System.Threading.Timeout.InfiniteTimeSpan))).IsFullyDrained);
                panel.ActionButton.onClick.Invoke();Assert.AreEqual(i+1,panel.ClickCount);
            }
        });
        [Test] public void ChangedSchemaRequiresGeneration(){manifest.Fields[0].Name="Renamed";Assert.Throws<InvalidOperationException>(()=>UiBindingGenerator.ValidateCompiled(manifest));}
        [Test] public void SceneObjectCannotBeSavedAsPrefabByBinder(){Assert.Throws<InvalidOperationException>(()=>UiPrefabBindings.BindPrefab(root));}
    }
}
