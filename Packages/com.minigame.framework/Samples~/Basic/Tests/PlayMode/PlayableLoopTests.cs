using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MiniGame.Examples.Basic.Tests
{
    public sealed class PlayableLoopTests
    {
        private Scene scene;
        private BasicGameView view;
        private string progressDirectory;

        [UnitySetUp]
        public IEnumerator Load()
        {
#if UNITY_EDITOR
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath("e9abd4f078c0c4840b62c299003b4ba8");
#else
            string path = "Assets/Examples/Basic/Scenes/Boot.unity";
#endif
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
#else
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
#endif
            scene = SceneManager.GetSceneByPath(path);
            Assert.IsTrue(scene.isLoaded);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                view = root.GetComponentInChildren<BasicGameView>(true);
                if (view) break;
            }
            Assert.IsNotNull(view);
            for (int frame = 0; frame < 120 && !view.IsReady; frame++) yield return null;
            Assert.IsTrue(view.IsReady, "Runtime did not initialize.");
            progressDirectory = System.IO.Path.Combine(Application.temporaryCachePath, "basic-test-" + System.Guid.NewGuid().ToString("N"));
            view.UseProgressDirectory(progressDirectory);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            if (progressDirectory != null && System.IO.Directory.Exists(progressDirectory)) System.IO.Directory.Delete(progressDirectory, true);
        }

        [UnityTest]
        public IEnumerator RealButtonsCompleteAndRestartRound()
        {
            view.StartButton.onClick.Invoke();
            Assert.AreEqual(LevelStatus.Playing, view.Current.Status);
            for (int i = 0; i < view.Current.Goal; i++) view.Cell(view.Current.Target).onClick.Invoke();
            Assert.AreEqual(LevelStatus.Won, view.Current.Status);
            var restored = new BasicProgressStore(progressDirectory);
            Assert.AreEqual(MiniGame.Framework.StoreStatus.Success, restored.Status);
            Assert.AreEqual(1, restored.Value.wonRounds);
            view.Cell(0).onClick.Invoke();
            Assert.AreEqual(1, new BasicProgressStore(progressDirectory).Value.completedRounds);
            yield return null;
            view.StartButton.onClick.Invoke();
            Assert.AreEqual(0, view.Current.Matches);
            Assert.AreEqual(LevelStatus.Playing, view.Current.Status);
        }

        [UnityTest]
        public IEnumerator DisabledViewDoesNotReceiveButtonActions()
        {
            view.StartButton.onClick.Invoke();
            long revision = view.Current.Revision;
            view.enabled = false;
            view.StartButton.onClick.Invoke();
            view.Cell(view.Current.Target).onClick.Invoke();
            Assert.AreEqual(revision, view.Current.Revision);
            yield return null;
            view.enabled = true;
            view.Cell(view.Current.Target).onClick.Invoke();
            Assert.Greater(view.Current.Revision, revision);
        }

        [UnityTest]
        public IEnumerator PauseBlocksActionsAndViewCanRebind()
        {
            view.StartButton.onClick.Invoke();
            view.TogglePause();
            int moves = view.Current.Moves;
            view.Cell(view.Current.Target).onClick.Invoke();
            Assert.AreEqual(moves, view.Current.Moves);
            view.TogglePause();
            view.gameObject.SetActive(false);
            yield return null;
            view.gameObject.SetActive(true);
            view.Cell(view.Current.Target).onClick.Invoke();
            Assert.AreEqual(moves - 1, view.Current.Moves);
        }
    }
}
