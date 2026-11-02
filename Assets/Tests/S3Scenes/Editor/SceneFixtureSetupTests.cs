using System.IO;
using System.Linq;
using MiniFramework.Scenes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
public sealed class SceneFixtureSetupTests
{
    [Test] public void PrepareOwnedFixtureScenes()
    {
        const string directory="Assets/__S304Fixture";
        if(Directory.Exists(directory)&&File.Exists(directory+"/owned-by-s304.txt")&&File.ReadAllText(directory+"/owned-by-s304.txt")=="S3-04 fixture; created by SceneFixtureSetupTests"&&Directory.GetFiles(directory,"*.unity").Length==0)AssetDatabase.DeleteAsset(directory);
        Assert.IsFalse(Directory.Exists(directory),"Refuse to overwrite an existing fixture directory.");
        // The EditMode runner owns an empty untitled scene. Refuse to discard any
        // unsaved scene content; saved scenes are restored by their captured setup.
        for(var i=0;i<SceneManager.sceneCount;i++)
        {
            var open=SceneManager.GetSceneAt(i);
            Assert.IsFalse(open.isDirty,"Unsaved scene must not be replaced by fixture setup.");
            if(string.IsNullOrEmpty(open.path))Assert.AreEqual(0,open.rootCount,"Untitled scene contains authored objects.");
        }
        foreach(var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if(go!=null&&go.name=="S304-A"&&EditorSceneManager.IsPreviewScene(go.scene))EditorSceneManager.ClosePreviewScene(go.scene);
        var setup=EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Directory.CreateDirectory(directory);File.WriteAllText(directory+"/owned-by-s304.txt","S3-04 fixture; created by SceneFixtureSetupTests");
            foreach(var name in new[]{"A","B"})
            {
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                SceneManager.MoveGameObjectToScene(new GameObject("S304-"+name),scene);
                Assert.IsTrue(EditorSceneManager.SaveScene(scene,directory+"/"+name+".unity"));
                // The next Single creation replaces this saved, clean fixture scene.
            }
            File.WriteAllText(directory+"/owned-by-s304.txt","S3-04 fixture; created by SceneFixtureSetupTests");AssetDatabase.Refresh();
        }
        finally{EditorSceneManager.RestoreSceneManagerSetup(setup);}
    }
    [Test] public void SceneAssemblyDoesNotReferenceEditorOrSdk()
    {Assert.IsFalse(typeof(SceneService).Assembly.GetReferencedAssemblies().Any(a=>a.Name.Contains("UnityEditor")||a.Name.Contains("YooAsset")||a.Name.StartsWith("MiniGameFramework")));}
}
