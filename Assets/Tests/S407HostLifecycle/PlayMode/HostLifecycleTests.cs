using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat;
using MiniFramework.Providers.WeChat.Storage;
using MiniFramework.S407HostLifecycle;
using MiniFramework.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MiniFramework.S407HostLifecycle.Tests
{
    public sealed class HostLifecycleTests
    {
        sealed class Signals : IWeChatApi
        {
            public readonly List<Action> Shows = new List<Action>(), Hides = new List<Action>();
            public PlatformExecutionMode Mode => PlatformExecutionMode.Mock;
            public void OnShow(Action c) => Shows.Add(c);
            public void OnHide(Action c) => Hides.Add(c);
            public void OffShow(Action c) => Shows.Remove(c);
            public void OffHide(Action c) => Hides.Remove(c);
            public void Initialize(Action<int> c) => c(0);
            public IWeChatLoginRequest CreateLogin(Action<string> s, Action f, Action c) => throw new NotSupportedException();
            public void Hide() { foreach (var c in Hides.ToArray()) c(); }
            public void Show() { foreach (var c in Shows.ToArray()) c(); }
        }
        sealed class Files : IWeChatStorageFiles
        {
            public readonly string Root = Path.Combine(Path.GetTempPath(), "mini-s407-hostlife-" + Guid.NewGuid().ToString("N"));
            public Files() { Directory.CreateDirectory(Root); }
            public StoragePersistence Persistence => StoragePersistence.Volatile;
            string Name(string key) => Path.Combine(Root, StorageFormat.Digest(System.Text.Encoding.UTF8.GetBytes(key)));
            public Result<StorageReadResult> Read(string key, int max)
            {
                var p = Name(key); if (!File.Exists(p)) return Result<StorageReadResult>.Success(new StorageReadResult(false));
                var bytes = File.ReadAllBytes(p); if (bytes.Length > max) throw new InvalidOperationException("fixture.limit");
                return Result<StorageReadResult>.Success(new StorageReadResult(true, bytes));
            }
            public Result Write(string key, byte[] bytes) { File.WriteAllBytes(Name(key), bytes); return Result.Success(); }
        }
        Signals signals; ObservedLifecycleApi api; Files files; HostLifecycleScenario scenario;
        static async UniTask Frames() { for (int i = 0; i < 8; i++) await UniTask.Yield(); }
        async UniTask Start()
        {
            signals = new Signals(); api = new ObservedLifecycleApi(signals); files = new Files();
            scenario = new HostLifecycleScenario(api, files, Guid.NewGuid().ToString("N")); await scenario.Start();
        }
        async UniTask GateCycle() { signals.Hide(); signals.Show(); await Frames(); Assert.AreEqual("gate_returned", scenario.Phase); await scenario.ArmPending(); }
        [UnityTearDown] public IEnumerator Cleanup() => UniTask.ToCoroutine(async () => {
            if (scenario != null) await scenario.Abort();
            if (files != null) Directory.Delete(files.Root, true);
            scenario = null; files = null;
        });
        [UnityTest] public IEnumerator OfficialAdapterShapeComposesGatePendingCloseAndFileRestore() => UniTask.ToCoroutine(async () => {
            await Start(); await GateCycle(); signals.Hide(); signals.Show(); await Frames(); await scenario.FinishAfterResume();
            var e = scenario.Evidence; Assert.IsTrue(e.passed, e.error); Assert.AreEqual(5, e.coins); Assert.AreEqual(1, e.claims);
            Assert.AreEqual(2, e.generation); Assert.AreEqual(1, e.claimOwnerGeneration); Assert.AreEqual(10, e.completedCallbacks);
            Assert.AreEqual(2, api.ShowOn); Assert.AreEqual(2, api.ShowOff); Assert.AreEqual(2, api.HideOn); Assert.AreEqual(2, api.HideOff);
            Assert.AreEqual(4, api.ControlledLateCallbacks); Assert.AreEqual(0, api.Listeners); Assert.IsTrue(e.closeWaited && e.observerCanceled && e.restored && e.lateFenced);
        });
        [UnityTest] public IEnumerator ShowWithoutHideCannotCertifyCycleOrCreateAd() => UniTask.ToCoroutine(async () => {
            await Start(); int writes = scenario.Writes; signals.Show(); signals.Show(); await Frames();
            Assert.AreEqual("await_gate_cycle", scenario.Phase); Assert.AreEqual(0, scenario.Evidence.gateCycles);
            Assert.AreEqual(writes, scenario.Writes); Assert.AreEqual(0, scenario.AdAttempts); Assert.IsFalse(scenario.Evidence.passed);
        });
        [UnityTest] public IEnumerator HideWithoutReturnDeniesWithoutWritingOrCompletingCycle() => UniTask.ToCoroutine(async () => {
            await Start(); int writes = scenario.Writes; signals.Hide(); await Frames();
            Assert.AreEqual("gate_hidden", scenario.Phase); Assert.AreEqual(0, scenario.Evidence.gateCycles);
            Assert.AreEqual(writes, scenario.Writes); Assert.AreEqual(0, scenario.AdAttempts); Assert.IsFalse(scenario.Pending); Assert.AreEqual(0, scenario.Coins);
        });
        [UnityTest] public IEnumerator RepeatedForegroundReturnsNeverReplaceACompletionFact() => UniTask.ToCoroutine(async () => {
            await Start(); await GateCycle(); int writes = scenario.Writes;
            for (int i = 0; i < 3; i++) { signals.Hide(); signals.Show(); await Frames(); }
            Assert.AreEqual("pending_returned", scenario.Phase); Assert.AreEqual(1, scenario.AdAttempts); Assert.AreEqual(writes, scenario.Writes);
            Assert.AreEqual(0, scenario.Coins); Assert.IsTrue(scenario.Pending); Assert.IsFalse(scenario.Evidence.passed);
            await scenario.FinishAfterResume(); Assert.IsTrue(scenario.Evidence.passed); Assert.AreEqual(5, scenario.Evidence.coins);
        });
    }
}
