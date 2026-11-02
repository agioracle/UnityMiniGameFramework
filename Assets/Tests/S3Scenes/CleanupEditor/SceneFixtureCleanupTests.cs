using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.SceneManagement;
public sealed class SceneFixtureCleanupTests
{
    [Test] public void RemoveOnlyOwnedUnloadedFixtures()
    {
        const string directory="Assets/__S304Fixture";
        Assert.AreEqual("S3-04 fixture; created by SceneFixtureSetupTests",File.ReadAllText(directory+"/owned-by-s304.txt"));
        for(var i=0;i<SceneManager.sceneCount;i++)Assert.IsFalse(SceneManager.GetSceneAt(i).path.StartsWith(directory));
        Assert.IsTrue(AssetDatabase.DeleteAsset(directory));AssetDatabase.Refresh();Assert.IsFalse(Directory.Exists(directory));
    }
}
