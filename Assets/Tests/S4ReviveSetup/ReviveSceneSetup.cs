using System.IO;
using System.Linq;
using Game.Boot;
using Game.Boot.Editor;
using NUnit.Framework;
using UnityEditor;
public sealed class ReviveSceneSetup
{
    [Test] public void InstallOwnedReviveSampleAndConfigureTestScene()
    {
        ReviveSampleInstaller.Install();var scenes=EditorBuildSettings.scenes;if(!scenes.Any(s=>s.path==ReviveSampleOptions.Scene)){File.WriteAllText("Library/S406AddedScene.txt",ReviveSampleOptions.Scene);EditorBuildSettings.scenes=scenes.Concat(new[]{new EditorBuildSettingsScene(ReviveSampleOptions.Scene,true)}).ToArray();AssetDatabase.SaveAssets();}
        Assert.IsTrue(File.Exists(ReviveSampleOptions.Scene));ReviveSampleInstaller.Validate();
    }
    [Test] public void InstallerIsIdempotentWithoutOriginalUiRewrite(){ReviveSampleInstaller.Install();var first=File.ReadAllBytes(ReviveSampleOptions.Scene);ReviveSampleInstaller.Install();CollectionAssert.AreEqual(first,File.ReadAllBytes(ReviveSampleOptions.Scene));}
}
