using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Access;
using Game.Revive;
using MiniFramework.Ads;
using MiniFramework.Contracts;
using MiniFramework.Ownership;
using MiniFramework.Platform;
using MiniFramework.Providers.AdsMock;
using MiniFramework.Providers.PlatformMock;
using MiniFramework.Providers.WeChat.Storage;
using MiniFramework.Runtime;
using MiniFramework.Storage;
using NUnit.Framework;
using ReviveTests;
using UnityEngine.TestTools;

namespace MiniFramework.S407Lifecycle.Tests
{
    // Editor-only test composition: actual scratch files, injected lifecycle and Mock ads.
    // This port does not certify the WX SDK, crash durability or OS memory pressure.
    public sealed class RewardLifecycleTests
    {
        static OperationContext Wait => new OperationContext(default, TimeSpan.FromSeconds(5));
        static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
        readonly List<Fixture> fixtures = new List<Fixture>();
        readonly List<string> roots = new List<string>();

        sealed class ScratchFiles : IWeChatStorageFiles
        {
            readonly string root;
            public int Writes { get; private set; }
            public StoragePersistence Persistence => StoragePersistence.Volatile;
            public ScratchFiles(string root) { this.root = root; Directory.CreateDirectory(root); }
            string PathFor(string key) => Path.Combine(root, StorageFormat.Digest(System.Text.Encoding.UTF8.GetBytes(key)) + ".bin");
            public Result<StorageReadResult> Read(string key, int maxBytes)
            {
                var path = PathFor(key);
                if (!File.Exists(path)) return Result<StorageReadResult>.Success(new StorageReadResult(false, null));
                if (new FileInfo(path).Length > maxBytes) return Result<StorageReadResult>.Failure(new FrameworkError(ErrorKind.Storage, "fixture.file.limit"));
                return Result<StorageReadResult>.Success(new StorageReadResult(true, File.ReadAllBytes(path)));
            }
            public Result Write(string key, byte[] bytes) { File.WriteAllBytes(PathFor(key), bytes); Writes++; return Result.Success(); }
        }

        sealed class Fixture
        {
            public readonly ScratchFiles Files;
            public readonly WeChatStorageProvider Provider;
            public readonly GateStorage Storage;
            public readonly ReviveRepository Store;
            public readonly DurableLocalOwnerModule Owner;
            public readonly MockPlatformBackend PlatformBackend;
            public readonly PlatformService Platform;
            public readonly MockRewardedAdBackend Backend = new MockRewardedAdBackend();
            public readonly AdReceiptJournal Facts = new AdReceiptJournal();
            public readonly AdsService Ads;
            public readonly ReviveService Revive;
            public readonly GameContext Context;
            public readonly StorageAddress Address = new StorageAddress("local.s407life", "reward");
            long sequence;
            public Fixture(string root)
            {
                Files = new ScratchFiles(root); Provider = new WeChatStorageProvider(Files); Storage = new GateStorage(Provider);
                Store = new ReviveRepository(Storage, ClaimOwnerMode.LocalOnly, "lifecycle-installation", Address, new StorageLimits(maxContainerBytes: 65536));
                Owner = new DurableLocalOwnerModule(Store, OwnershipPolicy.LocalOnly("lifecycle-installation", Address.Namespace));
                PlatformBackend = new MockPlatformBackend { Startup = new PlatformStartup(new PlatformCapabilities(new[] {
                    new PlatformCapability(PlatformFeature.Lifecycle, CapabilitySupport.Supported, CapabilityAvailability.Available, "fixture.lifecycle"),
                    new PlatformCapability(PlatformFeature.RewardedAds, CapabilitySupport.Supported, CapabilityAvailability.Available, "fixture.ads", CapabilityConstraints.ForegroundOnly | CapabilityConstraints.UserGestureRequired, 1)
                }), PlatformVisibility.Foreground) };
                Platform = new PlatformService(PlatformBackend);
                Revive = new ReviveService(Store, Facts, PlatformExecutionMode.Mock);
                Ads = new AdsService(Backend, Revive, ownerModuleId: Owner.Id);
                Context = new GameContext(new[] { Reg(Store, typeof(IRevivePersistenceView)), Reg(Owner, typeof(IClaimOwnerAuthority)),
                    Reg(Platform, typeof(IPlatformCapabilities), typeof(IPlatformLifecycle)), Reg(Ads, typeof(IAdsService)), Reg(Revive, typeof(IReviveService)) }, Cleanup);
            }
            static ModuleRegistration Reg(IModule module, params Type[] services) => new ModuleRegistration(module.Id, module.Dependencies, () => module, services);
            public MockPlatformSession Session => PlatformBackend.Sessions.Single();
            public MockRewardedAdAttempt Attempt => Backend.Attempts.Last();
            public async UniTask Start() { Check(await Context.StartAsync(Wait)); }
            public async UniTask FailedRun() { Check(await Revive.BeginRunAsync(1, Wait)); Check(await Revive.FailRunAsync(Wait)); }
            public IReviveOperation Capture() { var result = Revive.CaptureRevive(); Assert.IsTrue(result.IsSuccess); return result.Value; }
            public async UniTask Event(PlatformLifecycleKind kind)
            {
                Assert.IsTrue(Session.Publish(PlatformCallback.LifecycleChanged(Platform.Generation, ++sequence, kind)));
                await Frames();
            }
            public void Complete(int duplicates = 1, bool completed = true)
            {
                Assert.IsTrue(Attempt.Publish(AdCallbackKind.Started));
                if (completed) for (int i = 0; i < duplicates; i++) Assert.IsTrue(Attempt.Publish(AdCallbackKind.Completed));
                Assert.IsTrue(Attempt.Publish(AdCallbackKind.Closed)); Attempt.CompleteShow();
            }
            public async UniTask<UniTask<Result<ReviveResult>>> HoldShow()
            {
                Backend.HoldShow = true; Backend.AutoCallbacks = false;
                var task = Capture().WaitAsync(Wait).Preserve();
                await Until(() => Backend.Attempts.Count == 1 && Attempt.ShowCount == 1);
                Assert.IsNotNull(Read().Pending); Assert.AreEqual(0, Read().Coins);
                return task;
            }
            public byte[] Raw()
            {
                var candidates = new List<KeyValuePair<long, byte[]>>();
                for (int bank = 0; bank < 2; bank++)
                {
                    var read = Files.Read(WeChatStorageProvider.KeyFor(Address, bank), 65536); Assert.IsTrue(read.IsSuccess);
                    if (!read.Value.Exists) continue;
                    var raw = read.Value.CopyBytes(); var decoded = StorageFormat.Decode(raw, new StorageLimits()); Assert.IsTrue(decoded.IsSuccess);
                    Assert.AreEqual(Address.Namespace, decoded.Value.Address.Namespace); Assert.AreEqual(Address.Slot, decoded.Value.Address.Slot);
                    candidates.Add(new KeyValuePair<long, byte[]>(decoded.Value.Sequence, raw));
                }
                Assert.IsNotEmpty(candidates); return candidates.OrderByDescending(x => x.Key).First().Value;
            }
            public ReviveSnapshot Read()
            {
                var decoded = ReviveCodec.Decode(StorageFormat.Decode(Raw(), new StorageLimits()).Value.Records.Single().CopyPayload());
                Assert.IsTrue(decoded.IsSuccess); return decoded.Value;
            }
            public async UniTask Finish()
            {
                Storage.FailCleanup = false; Backend.FailCleanup = Backend.ThrowCleanup = Backend.HoldCleanup = false; Storage.Release();
                var close = Context.CloseAsync(Wait).Preserve();
                for (int i = 0; i < 12; i++) { Storage.Release(); foreach (var a in Backend.Attempts) { a.CompleteLoad(); a.CompleteShow(); a.CompleteCleanup(); } await UniTask.Yield(); }
                var result = await close;
                if (!result.IsSuccess) result = await Context.RetryCleanupAsync(Cleanup, Wait);
                Check(result); Assert.AreEqual(0, Context.RetainedModuleCount); Assert.AreEqual(0, Provider.OpenSessionCount);
                Assert.AreEqual(0, Ads.PendingCount); Assert.AreEqual(0, Facts.PendingCount);
            }
        }

        Fixture New(string existingRoot = null)
        {
            var root = existingRoot ?? Path.Combine(Path.GetTempPath(), "mini-s407-life-" + Guid.NewGuid().ToString("N"));
            if (existingRoot == null) roots.Add(root);
            var fixture = new Fixture(root); fixtures.Add(fixture); return fixture;
        }
        static void Check(Result result) => Assert.IsTrue(result.IsSuccess, result.IsSuccess ? "" : result.Error.Code);
        static async UniTask Frames() { for (int i = 0; i < 5; i++) await UniTask.Yield(); }
        static async UniTask Until(Func<bool> predicate) { for (int i = 0; i < 120; i++) { if (predicate()) return; await UniTask.Yield(); } Assert.Fail("Controlled operation did not reach expected phase."); }
        [UnityTearDown] public IEnumerator Teardown() => UniTask.ToCoroutine(async () => {
            foreach (var fixture in fixtures) await fixture.Finish(); fixtures.Clear();
            foreach (var root in roots) Directory.Delete(root, true); roots.Clear();
        });

        [UnityTest] public IEnumerator HiddenBeforeCaptureWritesNoIntentAndShownPermitsOneGrant() => UniTask.ToCoroutine(async () => {
            var e = New(); await e.Start(); await e.FailedRun(); await e.Event(PlatformLifecycleKind.Hidden);
            int writes = e.Files.Writes; var denied = await e.Capture().WaitAsync(Wait);
            Assert.IsFalse(denied.IsSuccess); Assert.AreEqual(ErrorKind.Unavailable, denied.Error.Kind);
            Assert.AreEqual(writes, e.Files.Writes); Assert.AreEqual(0, e.Backend.Attempts.Count); Assert.IsNull(e.Read().Pending);
            await e.Event(PlatformLifecycleKind.Shown); var result = await e.Capture().WaitAsync(Wait);
            Assert.IsTrue(result.IsSuccess && result.Value.Granted); Assert.AreEqual(5, e.Read().Coins); Assert.AreEqual(1, e.Read().Claims.Count);
        });
        [UnityTest] public IEnumerator HiddenWhileLoadingPreventsShowAndAllowsExplicitForegroundRetry() => UniTask.ToCoroutine(async () => {
            var e = New(); e.Backend.HoldLoad = true; await e.Start(); await e.FailedRun();
            var task = e.Capture().WaitAsync(Wait).Preserve(); await Until(() => e.Backend.Attempts.Count == 1 && e.Attempt.LoadCount == 1);
            await e.Event(PlatformLifecycleKind.Hidden); e.Attempt.CompleteLoad(); Assert.IsFalse((await task).IsSuccess);
            Assert.AreEqual(0, e.Attempt.ShowCount); Assert.AreEqual(0, e.Read().Coins); Assert.IsNull(e.Read().Pending);
            Assert.AreEqual(ReviveClaimState.Aborted, e.Read().Claims.Single().State);
            e.Backend.HoldLoad = false; await e.Event(PlatformLifecycleKind.Shown); Assert.IsTrue((await e.Capture().WaitAsync(Wait)).Value.Granted);
            Assert.AreEqual(5, e.Read().Coins); Assert.AreEqual(2, e.Backend.Attempts.Count);
        });
        [UnityTest] public IEnumerator BackgroundMatchedCompletionGrantsOnceAndReadsBackFromFiles() => UniTask.ToCoroutine(async () => {
            var e = New(); await e.Start(); await e.FailedRun(); var task = await e.HoldShow(); await e.Event(PlatformLifecycleKind.Hidden);
            e.Complete(10); Assert.IsTrue((await task).Value.Granted); Assert.AreEqual(5, e.Read().Coins); Assert.AreEqual(1, e.Read().Claims.Count);
            await e.Finish(); var reopened = New(roots.Single()); await reopened.Start();
            Assert.AreEqual(5, reopened.Store.Snapshot.Coins); Assert.AreEqual(2, reopened.Store.Snapshot.LastGeneration); Assert.AreEqual(1, reopened.Store.Snapshot.Claims.Single().Owner.SessionGeneration);
            Assert.IsNull(reopened.Read().Pending); Assert.AreEqual(0, reopened.Backend.Attempts.Count);
        });
        [UnityTest] public IEnumerator MemoryWarningIsNotCompletionAndDoesNotRewritePendingFiles() => UniTask.ToCoroutine(async () => {
            var e = New(); await e.Start(); await e.FailedRun(); var task = await e.HoldShow(); var before = e.Raw(); int writes = e.Files.Writes;
            await e.Event(PlatformLifecycleKind.MemoryWarning); Assert.AreEqual(PlatformVisibility.Foreground, e.Platform.Visibility);
            CollectionAssert.AreEqual(before, e.Raw()); Assert.AreEqual(writes, e.Files.Writes); Assert.AreEqual(UniTaskStatus.Pending, task.Status);
            e.Complete(completed: false); Assert.IsFalse((await task).Value.Granted); Assert.AreEqual(0, e.Read().Coins);
        });
        [UnityTest] public IEnumerator ForegroundReturnWithoutAdFactNeverGrants() => UniTask.ToCoroutine(async () => {
            var e = New(); await e.Start(); await e.FailedRun(); var task = await e.HoldShow();
            await e.Event(PlatformLifecycleKind.Hidden); await e.Event(PlatformLifecycleKind.Shown); await e.Event(PlatformLifecycleKind.Shown);
            Assert.AreEqual(0, e.Read().Coins); Assert.IsNotNull(e.Read().Pending); Assert.AreEqual(UniTaskStatus.Pending, task.Status); Assert.AreEqual(0, e.Attempt.CleanupCount);
            e.Complete(completed: false); Assert.IsFalse((await task).Value.Granted); Assert.AreEqual(0, e.Read().Coins);
        });
        [UnityTest] public IEnumerator RepeatedLifecycleEventsAndDuplicateFactsCannotRepeatGrant() => UniTask.ToCoroutine(async () => {
            var e = New(); await e.Start(); await e.FailedRun(); var task = await e.HoldShow();
            for (int i = 0; i < 3; i++) { await e.Event(PlatformLifecycleKind.Hidden); await e.Event(PlatformLifecycleKind.MemoryWarning); await e.Event(PlatformLifecycleKind.Shown); }
            e.Complete(10); Assert.IsTrue((await task).Value.Granted); int writes = e.Files.Writes;
            await e.Event(PlatformLifecycleKind.MemoryWarning); await e.Event(PlatformLifecycleKind.Hidden); await e.Event(PlatformLifecycleKind.Shown);
            Assert.AreEqual(writes, e.Files.Writes); Assert.AreEqual(5, e.Read().Coins); Assert.AreEqual(1, e.Read().Claims.Count); Assert.AreEqual(1, e.Attempt.ShowCount);
        });
        [UnityTest] public IEnumerator MemoryWarningDuringIntentWriteDoesNotBypassCommitBarrier() => UniTask.ToCoroutine(async () => {
            var e = New(); await e.Start(); await e.FailedRun(); e.Storage.HoldNext = true; var before = e.Raw();
            var task = e.Capture().WaitAsync(Wait).Preserve(); await Frames(); await e.Event(PlatformLifecycleKind.MemoryWarning);
            Assert.AreEqual(0, e.Backend.Attempts.Count); Assert.IsNull(e.Read().Pending); CollectionAssert.AreEqual(before, e.Raw());
            e.Storage.Release(); Assert.IsTrue((await task).Value.Granted); Assert.AreEqual(5, e.Read().Coins); Assert.AreEqual(1, e.Attempt.ShowCount);
        });
        [UnityTest] public IEnumerator CloseInBackgroundRetainsVisibleAttemptUntilActualCompletion() => UniTask.ToCoroutine(async () => {
            var e = New(); await e.Start(); await e.FailedRun(); var task = await e.HoldShow(); await e.Event(PlatformLifecycleKind.Hidden);
            var close = e.Context.CloseAsync(Wait).Preserve(); await Frames(); Assert.AreEqual(UniTaskStatus.Pending, close.Status);
            Assert.Greater(e.Context.RetainedModuleCount, 0); Assert.AreEqual(1, e.Provider.OpenSessionCount); Assert.AreEqual(0, e.Attempt.CleanupCount);
            e.Complete(); Check(await close); Assert.AreEqual(ErrorKind.Canceled, (await task).Error.Kind);
            Assert.AreEqual(5, e.Read().Coins); Assert.AreEqual(0, e.Context.RetainedModuleCount); Assert.AreEqual(0, e.Provider.OpenSessionCount);
        });
        [UnityTest] public IEnumerator BackgroundCleanupFailureNeedsExplicitRetryWithoutRewardReplay() => UniTask.ToCoroutine(async () => {
            var e = New(); await e.Start(); await e.FailedRun(); Assert.IsTrue((await e.Capture().WaitAsync(Wait)).Value.Granted);
            await e.Event(PlatformLifecycleKind.Hidden); e.Storage.FailCleanup = true; Assert.IsFalse((await e.Context.CloseAsync(Wait)).IsSuccess);
            Assert.Greater(e.Context.RetainedModuleCount, 0); Assert.AreEqual(1, e.Provider.OpenSessionCount); var before = e.Raw(); int writes = e.Files.Writes; int cleanups = e.Storage.Cleanups;
            Assert.IsFalse((await e.Context.CloseAsync(Wait)).IsSuccess); Assert.AreEqual(cleanups, e.Storage.Cleanups);
            e.Storage.FailCleanup = false; Check(await e.Context.RetryCleanupAsync(Cleanup, Wait));
            Assert.AreEqual(writes, e.Files.Writes); CollectionAssert.AreEqual(before, e.Raw()); Assert.AreEqual(5, e.Read().Coins); Assert.AreEqual(1, e.Attempt.ShowCount);
        });
        [UnityTest] public IEnumerator OldGenerationDuplicateSequenceAndClosedProducerCannotGateNewOwner() => UniTask.ToCoroutine(async () => {
            var e = New(); await e.Start(); await e.FailedRun(); await e.Event(PlatformLifecycleKind.MemoryWarning);
            Assert.IsTrue(e.Session.Publish(PlatformCallback.LifecycleChanged(e.Platform.Generation, 1, PlatformLifecycleKind.Hidden)));
            Assert.IsTrue(e.Session.Publish(PlatformCallback.LifecycleChanged("stale-generation", 999, PlatformLifecycleKind.Hidden)));
            await Frames(); Assert.AreEqual(PlatformVisibility.Foreground, e.Platform.Visibility); Assert.IsTrue((await e.Capture().WaitAsync(Wait)).Value.Granted);
            await e.Finish(); var reopened = New(roots.Single()); await reopened.Start();
            Assert.IsFalse(e.Session.Publish(PlatformCallback.LifecycleChanged(e.Platform.Generation, 1000, PlatformLifecycleKind.Hidden)));
            Assert.IsTrue(reopened.Session.Publish(PlatformCallback.LifecycleChanged(e.Platform.Generation, 1001, PlatformLifecycleKind.Hidden)));
            await Frames(); Assert.AreEqual(PlatformVisibility.Foreground, reopened.Platform.Visibility); Assert.AreEqual(5, reopened.Read().Coins); Assert.AreEqual(0, reopened.Backend.Attempts.Count);
        });
    }
}
