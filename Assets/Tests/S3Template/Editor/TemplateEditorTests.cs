using System.IO;
using System.Linq;
using Game.Boot;
using Game.Boot.Editor;
using Game.Features;
using MiniFramework.UI;
using MiniFramework.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
public sealed class TemplateEditorTests
{
    [OneTimeSetUp] public void Setup()=>LocalSampleInstaller.Install();
    [Test] public void InstallerIsIdempotent(){var before=EditorBuildSettings.scenes.Select(s=>s.path).ToArray();LocalSampleInstaller.Install();CollectionAssert.AreEqual(before,EditorBuildSettings.scenes.Select(s=>s.path).ToArray());}
    [Test] public void ExactlyThreeLocalScenesEnabled(){foreach(var path in new[]{SampleContent.BootstrapScene,SampleContent.TutorialScene,SampleContent.SecondScene})Assert.AreEqual(1,EditorBuildSettings.scenes.Count(s=>s.enabled&&s.path==path));}
    [Test] public void GeneratedBindingsStable(){foreach(var guid in AssetDatabase.FindAssets("t:UiBindingManifest",new[]{SampleContent.AssetRoot+"/Editor"})){var m=AssetDatabase.LoadAssetAtPath<UiBindingManifest>(AssetDatabase.GUIDToAssetPath(guid));Assert.AreEqual(File.ReadAllText(UiBindingGenerator.OutputPath(m)),UiBindingGenerator.Render(m));Assert.IsFalse(UiBindingGenerator.Generate(m));UiBindingGenerator.ValidateCompiled(m);}}
    [Test] public void PrefabBindingsAssigned(){Assert.IsTrue(Resources.Load<GameObject>(SampleContent.Root+"ui/home").GetComponent<SampleHomePanel>().ValidateGeneratedBindings().IsSuccess);Assert.IsTrue(Resources.Load<GameObject>(SampleContent.Root+"ui/level").GetComponent<SampleLevelPanel>().ValidateGeneratedBindings().IsSuccess);}
    [Test] public void HomeDoesNotStrongReferenceLevel(){var deps=AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(Resources.Load<GameObject>(SampleContent.Root+"ui/home")),true);Assert.IsFalse(deps.Any(p=>p.EndsWith(".unity")||p.EndsWith("/level.prefab")));}
    [Test] public void FirstPlayResourcesExist()=>LocalSampleInstaller.Validate();
    [Test] public void SampleFontCoversAscii(){var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");Assert.NotNull(font);for(var i=32;i<127;i++)Assert.IsTrue(font.HasCharacter((char)i),"glyph "+i);}
    [Test] public void ConfigBytesMatchGeneratedSource(){foreach(var name in new[]{"catalog.json","tblevel.bytes","tbreward.bytes"})CollectionAssert.AreEqual(File.ReadAllBytes("Assets/Game/ConfigData/"+name),File.ReadAllBytes(SampleContent.AssetRoot+"/Resources/"+SampleContent.Root+"config/"+name));}
}
