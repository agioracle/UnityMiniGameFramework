using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniGame.Framework;
using MiniGame.Examples.Basic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MiniGame.ReleaseValidation
{
    public static class ReleaseConsumer
    {
        [Serializable]
        public sealed class Data { public int value; public string owner; }

        public static void CheckFoundation(string directory, bool create)
        {
            using (var timers = new TimerService(4))
            using (var pauses = new PauseGate())
            using (var machine = new StateMachine<string>())
            using (var pool = new ObjectPool<object>(2, () => new object()))
            {
                int fired = 0;
                TimerHandle handle = timers.Schedule(.5, () => fired++);
                IDisposable pause = pauses.Acquire();
                timers.Tick(pauses.IsPaused ? 0 : .5, .5);
                Require(fired == 0 && handle.IsActive, "Pause failed.");
                pause.Dispose(); timers.Tick(.5, .5);
                Require(fired == 1 && !handle.IsActive, "Timer failed.");
                machine.Add("ready"); machine.Add("playing"); machine.Change("ready"); machine.Change("playing");
                Require(machine.Current == "playing", "State failed.");
                Require(pool.TryRent(out object item) && pool.Return(item) && !pool.Return(item), "Pool failed.");
            }
            var store = new LocalStore(directory, 4096);
            if (create) Require(store.Save("upgrade", 1, new Data { value = 37, owner = "consumer" }).Success, "Save failed.");
            StoreResult<Data> loaded = store.Load<Data>("upgrade", 1);
            Require(loaded.Success && loaded.Value.value == 37 && loaded.Value.owner == "consumer", "Upgrade changed consumer schema or data.");
        }

        public static string SampleScene()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.GUIDToAssetPath("e9abd4f078c0c4840b62c299003b4ba8");
#else
            return "Boot";
#endif
        }

        public static async UniTask CheckGame(string directory, bool create)
        {
#if UNITY_EDITOR
            AsyncOperation load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(SampleScene(), new LoadSceneParameters(LoadSceneMode.Additive));
#else
            AsyncOperation load = SceneManager.LoadSceneAsync(SampleScene(), LoadSceneMode.Additive);
#endif
            Require(load != null, "Sample scene missing.");
            while (!load.isDone) await UniTask.Yield();
            Scene scene = SceneManager.GetSceneByPath(SampleScene());
            if (!scene.IsValid()) scene = SceneManager.GetSceneByName("Boot");
            try
            {
                BasicGameView view = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    view = root.GetComponentInChildren<BasicGameView>(true);
                    if (view) break;
                }
                Require(view, "Sample bindings missing.");
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (!view.IsReady && Time.realtimeSinceStartupAsDouble < deadline) await UniTask.Yield();
                Require(view.IsReady, "Sample did not initialize.");
                view.UseProgressDirectory(directory);
                if (create)
                {
                    view.StartButton.onClick.Invoke();
                    Require(view.Current.Status == LevelStatus.Playing, "Start failed.");
                    for (int i = 0; i < view.Current.Goal; i++) view.Cell(view.Current.Target).onClick.Invoke();
                    Require(view.Current.Status == LevelStatus.Won, "Win failed.");
                }
                Require(new BasicProgressStore(directory).Value.wonRounds == 1, "Sample progress did not survive upgrade.");
            }
            finally
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                while (unload != null && !unload.isDone) await UniTask.Yield();
            }
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }

#if MINIGAME_RELEASE_SMOKE
    public sealed class ReleasePlayerProbe : MonoBehaviour
    {
        [Serializable]
        private sealed class Report { public bool passed; public string error; public string unityVersion; }
        private string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "-releaseEvidence") continue;
                string path = Path.GetFullPath(args[i + 1]);
                if (!path.Replace('\\', '/').Contains("/Builds/")) throw new ArgumentException("Evidence must be under Builds.");
                var root = new GameObject("Release evidence");
                DontDestroyOnLoad(root);
                root.AddComponent<ReleasePlayerProbe>().output = path;
                break;
            }
        }
        private void Start() => Run().Forget(Debug.LogException);
        private async UniTask Run()
        {
            var report = new Report { unityVersion = Application.unityVersion };
            try
            {
                string directory = Path.Combine(Path.GetDirectoryName(output), "player-save-" + Guid.NewGuid().ToString("N"));
                ReleaseConsumer.CheckFoundation(directory, true);
                BasicGameView view = null;
                foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    view = root.GetComponentInChildren<BasicGameView>(true);
                    if (view) break;
                }
                if (!view) throw new InvalidOperationException("Built sample missing.");
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (!view.IsReady && Time.realtimeSinceStartupAsDouble < deadline) await UniTask.Yield();
                if (!view.IsReady) throw new TimeoutException("Built sample not ready.");
                view.UseProgressDirectory(directory);
                view.StartButton.onClick.Invoke();
                for (int i = 0; i < view.Current.Goal; i++) view.Cell(view.Current.Target).onClick.Invoke();
                if (view.Current.Status != LevelStatus.Won || new BasicProgressStore(directory).Value.wonRounds != 1)
                    throw new InvalidOperationException("Built sample gameplay/save failed.");
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Application.Quit(report.passed ? 0 : 1);
        }
    }
#endif
}
