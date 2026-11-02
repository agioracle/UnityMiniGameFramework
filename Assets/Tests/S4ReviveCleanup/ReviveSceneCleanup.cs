using System.IO;
using System.Linq;
using Game.Boot;
using NUnit.Framework;
using UnityEditor;
public sealed class ReviveSceneCleanup
{
    [Test] public void RestoreBuildSettingsEntryOwnedByFixture(){if(File.Exists("Library/S406AddedScene.txt")){Assert.AreEqual(ReviveSampleOptions.Scene,File.ReadAllText("Library/S406AddedScene.txt"));EditorBuildSettings.scenes=EditorBuildSettings.scenes.Where(s=>s.path!=ReviveSampleOptions.Scene).ToArray();AssetDatabase.SaveAssets();Assert.IsFalse(EditorBuildSettings.scenes.Any(s=>s.path==ReviveSampleOptions.Scene));File.WriteAllText("Library/S406CleanupProof.json","{\"markerExisted\":true,\"entryAbsentInMemory\":true,\"entryAbsentOnDisk\":"+(!File.ReadAllText("ProjectSettings/EditorBuildSettings.asset").Contains(ReviveSampleOptions.Scene)?"true":"false")+"}");File.Delete("Library/S406AddedScene.txt");}Assert.IsTrue(File.Exists(ReviveSampleOptions.Scene));}
}
