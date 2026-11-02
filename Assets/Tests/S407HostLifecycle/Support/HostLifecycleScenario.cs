using System;
using System.Collections.Generic;
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
using MiniFramework.Providers.WeChat;
using MiniFramework.Providers.WeChat.Storage;
using MiniFramework.Runtime;
using MiniFramework.Storage;
using UnityEngine.Scripting;

namespace MiniFramework.S407HostLifecycle
{
    // Validation-only capability overlay. Mock ad identity stays Mock; native lifecycle
    // provenance is reported separately. No real advertising capability is granted.
    sealed class LifecycleMockComposition : IPlatformBackend
    {
        readonly IPlatformBackend source;
        public PlatformDescriptor Descriptor { get; } = new PlatformDescriptor("wechat", PlatformExecutionMode.Mock, "host-life-fixture-v1");
        public LifecycleMockComposition(IWeChatApi api) { source = new WeChatBackend(api); }
        public IPlatformSession Open(string generation, IPlatformCallbackSink sink) => new Session(source.Open(generation, sink));
        sealed class Session : IPlatformSession
        {
            readonly IPlatformSession source;
            internal Session(IPlatformSession source) { this.source = source; }
            public async UniTask<Result<PlatformStartup>> InitializeAsync()
            {
                var r = await source.InitializeAsync(); if (!r.IsSuccess) return r;
                var caps = Enum.GetValues(typeof(PlatformFeature)).Cast<PlatformFeature>().Select(f => f == PlatformFeature.RewardedAds
                    ? new PlatformCapability(f, CapabilitySupport.Supported, CapabilityAvailability.Available, "fixture.mock_ads", CapabilityConstraints.ForegroundOnly | CapabilityConstraints.UserGestureRequired, 1)
                    : r.Value.Capabilities.Get(f));
                return Result<PlatformStartup>.Success(new PlatformStartup(new PlatformCapabilities(caps), r.Value.Visibility, r.Value.Sequence));
            }
            public IPlatformLoginAttempt CreateLogin(string id, PlatformLoginOptions options) => throw new NotSupportedException("No login in validation composition.");
            public UniTask<Result> CleanupAsync(CleanupContext cleanup) => source.CleanupAsync(cleanup);
        }
    }
    [Serializable, Preserve] public sealed class HostLifecycleEvent
    {
        [Preserve] public string kind, generation, visibility, phase;
        [Preserve] public long sequence;
    }
    [Serializable, Preserve] public sealed class HostLifecycleEvidence
    {
        [Preserve] public string phase = "created", error = "", digest = "", backgroundError = "", backgroundCapabilityReason = "";
        [Preserve] public bool passed, backgroundDenied, backgroundUnchanged, resumeWithoutFact, closeWaited, observerCanceled, restored, lateFenced;
        [Preserve] public int gateCycles, pendingCycles, writes, completedCallbacks, modules, sessions, adsPending, factsPending, platformQueued, platformListeners;
        [Preserve] public long coins, generation, claimOwnerGeneration;
        [Preserve] public int claims;
        [Preserve] public List<string> stages = new List<string>();
        [Preserve] public List<HostLifecycleEvent> events = new List<HostLifecycleEvent>();
    }
    public sealed class HostLifecycleScenario
    {
        const string Ns = "local.s407hostlife", OwnerId = "host-life-installation";
        static OperationContext Wait => OperationContext.Unlimited();
        static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
        public readonly HostLifecycleEvidence Evidence = new HostLifecycleEvidence();
        readonly ObservedLifecycleApi api;
        readonly CountFiles files;
        readonly StorageAddress address;
        Env current, reopened;
        Scope observer = new Scope("host-life-observer");
        UniTask<Result<ReviveResult>> gateTask, pendingTask;
        bool gateTaskCreated, pendingTaskCreated;
        int gateWrites;
        string gateDigest;
        public string Phase => Evidence.phase;
        public int AdAttempts => current?.Backend.Attempts.Count ?? 0;
        public int Writes => files.Writes;
        public long Coins => current?.Store.Snapshot.Coins ?? 0;
        public bool Pending => current?.Store.Snapshot.Pending != null;
        sealed class CountFiles : IWeChatStorageFiles, IWeChatStorageCleanup
        {
            readonly IWeChatStorageFiles inner; public int Writes;
            public CountFiles(IWeChatStorageFiles inner) { this.inner = inner; }
            public StoragePersistence Persistence => inner.Persistence;
            public bool CleanupPending => inner is IWeChatStorageCleanup c && c.CleanupPending;
            public Result RetryCleanup() => inner is IWeChatStorageCleanup c ? c.RetryCleanup() : Result.Success();
            public Result<StorageReadResult> Read(string key, int max) => inner.Read(key, max);
            public Result Write(string key, byte[] bytes) { Writes++; return inner.Write(key, bytes); }
        }
        sealed class Env
        {
            public readonly WeChatStorageProvider Provider;
            public readonly ReviveRepository Store;
            public readonly PlatformService Platform;
            public readonly MockRewardedAdBackend Backend;
            public readonly AdsService Ads;
            public readonly AdReceiptJournal Facts = new AdReceiptJournal();
            public readonly ReviveService Revive;
            public readonly GameContext Context;
            public Env(ObservedLifecycleApi api, IWeChatStorageFiles files, StorageAddress address)
            {
                Provider = new WeChatStorageProvider(files, new WeChatStorageOptions(1048576));
                Store = new ReviveRepository(Provider, ClaimOwnerMode.LocalOnly, OwnerId, address, new StorageLimits(maxContainerBytes: 1048576));
                var owner = new DurableLocalOwnerModule(Store, OwnershipPolicy.LocalOnly(OwnerId, Ns));
                var mixed = new LifecycleMockComposition(api);
                Platform = new PlatformService(mixed);
                Backend = new MockRewardedAdBackend { Descriptor = mixed.Descriptor, HoldShow = true, AutoCallbacks = false };
                Revive = new ReviveService(Store, Facts, PlatformExecutionMode.Mock);
                Ads = new AdsService(Backend, Revive, ownerModuleId: owner.Id);
                Context = new GameContext(new[] { Reg(Store, typeof(IRevivePersistenceView)), Reg(owner, typeof(IClaimOwnerAuthority)),
                    Reg(Platform, typeof(IPlatformCapabilities), typeof(IPlatformLifecycle)), Reg(Ads, typeof(IAdsService)), Reg(Revive, typeof(IReviveService)) }, Cleanup);
            }
            static ModuleRegistration Reg(IModule m, params Type[] services) => new ModuleRegistration(m.Id, m.Dependencies, () => m, services);
        }
        public HostLifecycleScenario(ObservedLifecycleApi api, IWeChatStorageFiles files, string runId)
        { this.api = api; this.files = new CountFiles(files); address = new StorageAddress(Ns, "run-" + runId); }
        static void Require(bool value, string code) { if (!value) throw new InvalidOperationException(code); }
        static void Check(Result r) { Require(r.IsSuccess, r.IsSuccess ? "" : r.Error.Code); }
        static T Value<T>(Result<T> r) { Require(r.IsSuccess, r.IsSuccess ? "" : r.Error.Code); return r.Value; }
        void Stage(string name) { Evidence.stages.Add(name); }
        static async UniTask Frames() { for (int i = 0; i < 6; i++) await UniTask.Yield(); }
        static async UniTask Until(Func<bool> predicate, string code)
        { for (int i = 0; i < 600; i++) { if (predicate()) return; await UniTask.Yield(); } throw new InvalidOperationException(code); }
        byte[] Raw()
        {
            byte[] best = null; long sequence = -1;
            for (int bank = 0; bank < 2; bank++)
            {
                var r = Value(files.Read(WeChatStorageProvider.KeyFor(address, bank), 1048576)); if (!r.Exists) continue;
                var bytes = r.CopyBytes(); var c = Value(StorageFormat.Decode(bytes, new StorageLimits()));
                Require(c.Address.Namespace == Ns && c.Address.Slot == address.Slot, "file.address");
                if (c.Sequence > sequence) { sequence = c.Sequence; best = bytes; }
            }
            Require(best != null, "file.missing"); return best;
        }
        ReviveSnapshot Read() => Value(ReviveCodec.Decode(Value(StorageFormat.Decode(Raw(), new StorageLimits())).Records.Single().CopyPayload()));
        public async UniTask Start()
        {
            current = new Env(api, files, address); Check(await current.Context.StartAsync(Wait));
            Value(current.Platform.SubscribeLifecycle(observer, "host-life", OnLifecycle));
            Check(await current.Revive.BeginRunAsync(1, Wait)); Check(await current.Revive.FailRunAsync(Wait));
            Require(Coins == 0 && !Pending, "initial.state"); Stage("initialized"); Evidence.phase = "await_gate_cycle";
        }
        void OnLifecycle(PlatformLifecycleEvent e)
        {
            try
            {
                Require(Evidence.events.Count < 64, "event.limit");
                Evidence.events.Add(new HostLifecycleEvent { kind = e.Kind.ToString(), generation = e.Generation, sequence = e.Sequence, visibility = e.Visibility.ToString(), phase = Phase });
                if (e.Kind == PlatformLifecycleKind.Hidden && Phase == "await_gate_cycle")
                {
                    Require(current.Platform.Visibility == PlatformVisibility.Background, "hidden.visibility");
                    gateWrites = files.Writes; gateDigest = StorageFormat.Digest(Raw());
                    Evidence.backgroundCapabilityReason = current.Platform.Capabilities.Get(PlatformFeature.RewardedAds).ReasonCode;
                    gateTask = Value(current.Revive.CaptureRevive()).WaitAsync(Wait).Preserve(); gateTaskCreated = true; Evidence.phase = "gate_hidden";
                }
                else if (e.Kind == PlatformLifecycleKind.Shown && Phase == "gate_hidden") { Evidence.gateCycles++; Evidence.phase = "gate_returned"; }
                else if (e.Kind == PlatformLifecycleKind.Hidden && Phase == "await_pending_cycle")
                { Require(Coins == 0 && Pending && pendingTask.Status == UniTaskStatus.Pending, "pending.hidden"); Evidence.phase = "pending_hidden"; }
                else if (e.Kind == PlatformLifecycleKind.Shown && Phase == "pending_hidden") { Evidence.pendingCycles++; Evidence.phase = "pending_returned"; }
            }
            catch (Exception ex) { Evidence.error = ex.Message; Evidence.phase = "failed"; }
        }
        public async UniTask ArmPending()
        {
            Require(Phase == "gate_returned" && gateTaskCreated, "gate.cycle.required");
            var denied = await gateTask;
            Evidence.backgroundError = denied.IsSuccess ? "" : denied.Error.Code;
            Evidence.backgroundDenied = !denied.IsSuccess && denied.Error.Kind == ErrorKind.Unavailable && denied.Error.Code == "ads.unavailable" && Evidence.backgroundCapabilityReason == "platform.foreground.required";
            Evidence.backgroundUnchanged = files.Writes == gateWrites && StorageFormat.Digest(Raw()) == gateDigest && AdAttempts == 0 && !Pending;
            Require(Evidence.backgroundDenied && Evidence.backgroundUnchanged, "background.gate"); Stage("background_gate");
            Evidence.phase = "arming_pending";
            pendingTask = Value(current.Revive.CaptureRevive()).WaitAsync(Wait).Preserve(); pendingTaskCreated = true;
            await Until(() => AdAttempts == 1 && current.Backend.Attempts[0].ShowCount == 1, "mock.show.pending");
            Require(Read().Coins == 0 && Read().Pending != null, "pending.disk"); Stage("pending_held"); Evidence.phase = "await_pending_cycle";
        }
        public async UniTask FinishAfterResume()
        {
            Require(Phase == "pending_returned" && pendingTaskCreated, "pending.cycle.required");
            Evidence.resumeWithoutFact = Coins == 0 && Read().Coins == 0 && Pending && pendingTask.Status == UniTaskStatus.Pending;
            Require(Evidence.resumeWithoutFact, "resume.not_completion"); Stage("resume_no_fact"); Evidence.phase = "closing_pending";
            var attempt = current.Backend.Attempts.Single();
            var close = current.Context.CloseAsync(Wait).Preserve(); await Frames();
            Evidence.closeWaited = close.Status == UniTaskStatus.Pending && current.Context.RetainedModuleCount > 0 && current.Provider.OpenSessionCount == 1 && attempt.CleanupCount == 0;
            Require(Evidence.closeWaited, "close.barrier"); Stage("close_barrier");
            Require(attempt.Publish(AdCallbackKind.Started), "mock.started");
            for (int i = 0; i < 10; i++) { Require(attempt.Publish(AdCallbackKind.Completed), "mock.completed"); Evidence.completedCallbacks++; }
            Require(attempt.Publish(AdCallbackKind.Closed) && attempt.CompleteShow(), "mock.closed");
            Check(await close); var observed = await pendingTask;
            Evidence.observerCanceled = !observed.IsSuccess && observed.Error.Kind == ErrorKind.Canceled;
            Require(Evidence.observerCanceled && Read().Coins == 5 && Read().Pending == null && Read().Claims.Count == 1 && Read().Claims.Single().AdMode == PlatformExecutionMode.Mock, "mock.once"); Stage("mock_duplicate_commit");
            Require((await observer.CloseAsync(Cleanup)).IsFullyDrained, "observer.close");
            var eventCount = Evidence.events.Count; var before = StorageFormat.Digest(Raw()); api.InjectRetiredCallbacks(); await Frames();
            Evidence.lateFenced = !attempt.Publish(AdCallbackKind.Completed) && current.Platform.QueuedCount == 0 && Evidence.events.Count == eventCount && StorageFormat.Digest(Raw()) == before;
            Require(Evidence.lateFenced, "late.fenced");
            reopened = new Env(api, files, address); Check(await reopened.Context.StartAsync(Wait));
            var restored = reopened.Store.Snapshot;
            Evidence.restored = restored.Coins == 5 && restored.Pending == null && restored.Claims.Count == 1 && restored.LastGeneration == 2 && restored.Claims.Single().Owner.SessionGeneration == 1 && reopened.Backend.Attempts.Count == 0;
            Require(Evidence.restored, "file.restore"); Stage("same_process_restore"); Check(await reopened.Context.CloseAsync(Wait));
            api.InjectRetiredCallbacks(); await Frames(); Require(current.Platform.QueuedCount == 0 && reopened.Platform.QueuedCount == 0 && Evidence.events.Count == eventCount, "late.reopened"); Stage("late_callbacks_fenced");
            var final = Read(); Evidence.coins = final.Coins; Evidence.generation = final.LastGeneration; Evidence.claimOwnerGeneration = final.Claims.Single().Owner.SessionGeneration; Evidence.claims = final.Claims.Count;
            Evidence.digest = StorageFormat.Digest(Raw()); Evidence.writes = files.Writes; Count();
            Require(Evidence.modules == 0 && Evidence.sessions == 0 && Evidence.adsPending == 0 && Evidence.factsPending == 0 && Evidence.platformQueued == 0 && Evidence.platformListeners == 0 && api.Listeners == 0, "shutdown.retained");
            Stage("shutdown"); Evidence.passed = true; Evidence.phase = "completed";
        }
        void Count()
        {
            Evidence.modules = (current?.Context.RetainedModuleCount ?? 0) + (reopened?.Context.RetainedModuleCount ?? 0);
            Evidence.sessions = (current?.Provider.OpenSessionCount ?? 0) + (reopened?.Provider.OpenSessionCount ?? 0);
            Evidence.adsPending = (current?.Ads.PendingCount ?? 0) + (reopened?.Ads.PendingCount ?? 0);
            Evidence.factsPending = (current?.Facts.PendingCount ?? 0) + (reopened?.Facts.PendingCount ?? 0);
            Evidence.platformQueued = (current?.Platform.QueuedCount ?? 0) + (reopened?.Platform.QueuedCount ?? 0);
            Evidence.platformListeners = (current?.Platform.ReadDiagnostics().Listeners ?? 0) + (reopened?.Platform.ReadDiagnostics().Listeners ?? 0);
        }
        public async UniTask Abort()
        {
            foreach (var e in new[] { current, reopened })
            {
                if (e == null) continue;
                foreach (var a in e.Backend.Attempts) { a.Publish(AdCallbackKind.Closed); a.CompleteLoad(); a.CompleteShow(); a.CompleteCleanup(); }
                var r = await e.Context.CloseAsync(Wait); if (!r.IsSuccess) r = await e.Context.RetryCleanupAsync(Cleanup, Wait); Check(r);
            }
            Require((await observer.CloseAsync(Cleanup)).IsFullyDrained, "abort.observer"); Count();
        }
    }
}
