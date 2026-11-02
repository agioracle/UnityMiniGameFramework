using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
using UnityEngine;
using UnityEngine.Scripting;

namespace MiniFramework.S407RestartRecovery
{
    [Serializable, Preserve] public sealed class RestartCheckpoint
    {
        [Preserve] public int schemaVersion = 1, phase;
        [Preserve] public string buildId, chainId, runId, launchId, digest;
        [Preserve] public long sequence, generation;
    }
    [Serializable, Preserve] public sealed class RestartEvidence
    {
        [Preserve] public string phase = "created", error = "", chainId = "", previousRunId = "", previousLaunchId = "", previousDigest = "", digest = "", recoveryError = "";
        [Preserve] public int checkpoint, previousCheckpoint, writes, claims, grantedClaims, abortedClaims, pendingClaims, adAttempts, completedCallbacks, injectedWriteFaults;
        [Preserve] public int modules, sessions, adsPending, factsPending, platformQueued, platformListeners;
        [Preserve] public long sequence, previousSequence, generation, previousGeneration, claimOwnerGeneration, coins;
        [Preserve] public bool passed, chainComplete, priorBytesMatched, pendingWithoutProofPreserved, noAutomaticGrant, explicitAbandon, duplicateRecoveryUnchanged;
        [Preserve] public List<string> stages = new List<string>();
    }
    // Validation-only: three invocations must be separated by actual runtime replacement
    // in host acceptance. Local tests explicitly use synthetic launch IDs.
    public sealed class RestartRecoveryScenario
    {
        public const string Build = "s407-restart-csharp-v1-20261031", MarkerKey = "restart-checkpoint-v1";
        const string Ns = "local.s407restart", OwnerId = "restart-installation";
        const int Limit = 1048576;
        static OperationContext Wait => OperationContext.Unlimited();
        static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
        public readonly RestartEvidence Evidence = new RestartEvidence();
        readonly FaultFiles files;
        readonly string runId, launchId;
        StorageAddress address;
        Env env;
        sealed class FaultFiles : IWeChatStorageFiles, IWeChatStorageCleanup
        {
            readonly IWeChatStorageFiles inner;
            public bool LoseNextReceipt; public int Writes, Faults;
            public FaultFiles(IWeChatStorageFiles inner) { this.inner = inner; }
            public StoragePersistence Persistence => inner.Persistence;
            public bool CleanupPending => inner is IWeChatStorageCleanup c && c.CleanupPending;
            public Result RetryCleanup() => inner is IWeChatStorageCleanup c ? c.RetryCleanup() : Result.Success();
            public Result<StorageReadResult> Read(string key, int max) => inner.Read(key, max);
            public Result Write(string key, byte[] bytes)
            {
                Writes++; var r = inner.Write(key, bytes);
                if (r.IsSuccess && LoseNextReceipt) { LoseNextReceipt = false; Faults++; return Result.Failure(new FrameworkError(ErrorKind.Storage, "fixture.intent.receipt_lost")); }
                return r;
            }
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
            public Env(IWeChatStorageFiles files, StorageAddress address)
            {
                Provider = new WeChatStorageProvider(files, new WeChatStorageOptions(Limit));
                Store = new ReviveRepository(Provider, ClaimOwnerMode.LocalOnly, OwnerId, address, new StorageLimits(maxContainerBytes: Limit));
                var owner = new DurableLocalOwnerModule(Store, OwnershipPolicy.LocalOnly(OwnerId, Ns));
                var mock = new MockPlatformBackend { Startup = new PlatformStartup(new PlatformCapabilities(new[] {
                    new PlatformCapability(PlatformFeature.RewardedAds, CapabilitySupport.Supported, CapabilityAvailability.Available,
                        "fixture.mock_ads", CapabilityConstraints.ForegroundOnly | CapabilityConstraints.UserGestureRequired, 1)
                }), PlatformVisibility.Foreground) };
                Platform = new PlatformService(mock);
                Backend = new MockRewardedAdBackend { Descriptor = mock.Descriptor, HoldShow = true, AutoCallbacks = false };
                Revive = new ReviveService(Store, Facts, PlatformExecutionMode.Mock);
                Ads = new AdsService(Backend, Revive, ownerModuleId: owner.Id);
                Context = new GameContext(new[] { Reg(Store, typeof(IRevivePersistenceView)), Reg(owner, typeof(IClaimOwnerAuthority)),
                    Reg(Platform, typeof(IPlatformCapabilities), typeof(IPlatformLifecycle)), Reg(Ads, typeof(IAdsService)), Reg(Revive, typeof(IReviveService)) }, Cleanup);
            }
            static ModuleRegistration Reg(IModule m, params Type[] services) => new ModuleRegistration(m.Id, m.Dependencies, () => m, services);
        }
        public RestartRecoveryScenario(IWeChatStorageFiles files, string runId, string launchId)
        { this.files = new FaultFiles(files); this.runId = runId; this.launchId = launchId; }
        static void Require(bool v, string code) { if (!v) throw new InvalidOperationException(code); }
        static void Check(Result r) { Require(r.IsSuccess, r.IsSuccess ? "" : r.Error.Code); }
        static T Value<T>(Result<T> r) { Require(r.IsSuccess, r.IsSuccess ? "" : r.Error.Code); return r.Value; }
        void Stage(string s) => Evidence.stages.Add(s);
        byte[] Raw()
        {
            byte[] best = null; long seq = -1;
            for (int bank = 0; bank < 2; bank++)
            {
                var read = Value(files.Read(WeChatStorageProvider.KeyFor(address, bank), Limit)); if (!read.Exists) continue;
                var b = read.CopyBytes(); var c = Value(StorageFormat.Decode(b, new StorageLimits(maxContainerBytes: Limit)));
                Require(c.Address.Namespace == Ns && c.Address.Slot == address.Slot, "checkpoint.address");
                if (c.Sequence > seq) { seq = c.Sequence; best = b; }
            }
            Require(best != null, "checkpoint.file_missing"); return best;
        }
        ReviveSnapshot Read() => Value(ReviveCodec.Decode(Value(StorageFormat.Decode(Raw(), new StorageLimits(maxContainerBytes: Limit))).Records.Single().CopyPayload()));
        long Sequence() => Value(StorageFormat.Decode(Raw(), new StorageLimits(maxContainerBytes: Limit))).Sequence;
        public async UniTask Execute()
        {
            Require(!string.IsNullOrEmpty(runId) && !string.IsNullOrEmpty(launchId), "checkpoint.identity");
            var marker = Value(files.Read(MarkerKey, 16384)); RestartCheckpoint previous = null;
            if (marker.Exists)
            {
                previous = JsonUtility.FromJson<RestartCheckpoint>(new UTF8Encoding(false, true).GetString(marker.CopyBytes()));
                Require(previous != null && previous.schemaVersion == 1 && previous.buildId == Build && Guid.TryParseExact(previous.chainId, "N", out _) &&
                    previous.phase >= 1 && previous.phase <= 3 && !string.IsNullOrEmpty(previous.runId) && !string.IsNullOrEmpty(previous.launchId) &&
                    previous.digest != null && previous.digest.Length == 64 && previous.sequence > 0 && previous.generation > 0, "checkpoint.invalid");
                Require(previous.runId != runId && previous.launchId != launchId, "checkpoint.fresh_runtime_required");
                Evidence.chainId = previous.chainId; Evidence.previousRunId = previous.runId; Evidence.previousLaunchId = previous.launchId;
                Evidence.previousDigest = previous.digest; Evidence.previousSequence = previous.sequence; Evidence.previousCheckpoint = previous.phase; Evidence.previousGeneration = previous.generation;
            }
            else Evidence.chainId = Guid.NewGuid().ToString("N");
            address = new StorageAddress(Ns, "run-" + Evidence.chainId);
            if (previous != null)
            {
                Require(StorageFormat.Digest(Raw()) == previous.digest && Sequence() == previous.sequence && Read().LastGeneration == previous.generation, "checkpoint.prior_bytes_mismatch");
                Evidence.priorBytesMatched = true; Stage("previous_bytes_matched");
            }
            env = new Env(files, address); Check(await env.Context.StartAsync(Wait)); Stage("runtime_started");
            Require(env.Store.Snapshot.LastGeneration == (previous?.generation ?? 0) + 1, "generation.reserve");
            if (previous == null) await SavePending();
            else if (previous.phase == 1) await RecoverAndGrant();
            else await VerifyGranted();
            Check(await env.Context.CloseAsync(Wait)); Count();
            Require(Evidence.modules == 0 && Evidence.sessions == 0 && Evidence.adsPending == 0 && Evidence.factsPending == 0 && Evidence.platformQueued == 0 && Evidence.platformListeners == 0 && !files.CleanupPending, "shutdown.retained"); Stage("shutdown");
            var final = Read(); Evidence.digest = StorageFormat.Digest(Raw()); Evidence.sequence = Sequence(); Evidence.coins = final.Coins; Evidence.generation = final.LastGeneration;
            Evidence.claims = final.Claims.Count; Evidence.pendingClaims = final.Claims.Count(c => c.State == ReviveClaimState.Pending);
            Evidence.grantedClaims = final.Claims.Count(c => c.State == ReviveClaimState.Granted); Evidence.abortedClaims = final.Claims.Count(c => c.State == ReviveClaimState.Aborted);
            Evidence.claimOwnerGeneration = final.Claims.Single(c => c.State == (Evidence.checkpoint == 1 ? ReviveClaimState.Pending : ReviveClaimState.Granted)).Owner.SessionGeneration;
            var next = new RestartCheckpoint { buildId = Build, phase = Evidence.checkpoint, chainId = Evidence.chainId, runId = runId, launchId = launchId,
                digest = Evidence.digest, sequence = Evidence.sequence, generation = Evidence.generation };
            var raw = Encoding.UTF8.GetBytes(JsonUtility.ToJson(next)); Check(files.Write(MarkerKey, raw));
            Require(Value(files.Read(MarkerKey, 16384)).CopyBytes().SequenceEqual(raw), "checkpoint.marker_readback"); Stage("checkpoint_written");
            Evidence.writes = files.Writes; Evidence.injectedWriteFaults = files.Faults; Evidence.passed = true;
            Evidence.phase = Evidence.checkpoint == 1 ? "pending_checkpoint" : Evidence.checkpoint == 2 ? "reward_checkpoint" : "verified";
        }
        async UniTask SavePending()
        {
            Check(await env.Revive.BeginRunAsync(1, Wait)); Check(await env.Revive.FailRunAsync(Wait));
            files.LoseNextReceipt = true;
            var r = await Value(env.Revive.CaptureRevive()).WaitAsync(Wait);
            Require(!r.IsSuccess && env.Store.RecoveryRequired && env.Backend.Attempts.Count == 0 && files.Faults == 1, "intent.indeterminate_required");
            var disk = Read(); Require(disk.Coins == 0 && disk.Pending != null && disk.Claims.Count == 1 && disk.Pending.Owner.SessionGeneration == 1, "intent.actual_pending");
            Evidence.noAutomaticGrant = true; Stage("actual_pending_receipt_lost"); Evidence.checkpoint = 1;
        }
        async UniTask RecoverAndGrant()
        {
            var before = StorageFormat.Digest(Raw()); int writes = files.Writes;
            Require(env.Store.Snapshot.Pending != null && env.Store.Snapshot.Pending.Owner.SessionGeneration == Evidence.previousGeneration && env.Store.Snapshot.Coins == 0, "pending.restore"); Stage("pending_restored");
            var recovery = await env.Revive.RecoverPendingAsync(Wait); Evidence.recoveryError = recovery.IsSuccess ? "" : recovery.Error.Code;
            Evidence.pendingWithoutProofPreserved = !recovery.IsSuccess && recovery.Error.Code == "revive.recovery.no_proof" && StorageFormat.Digest(Raw()) == before && files.Writes == writes && Read().Pending != null;
            Evidence.noAutomaticGrant = Read().Coins == 0 && env.Backend.Attempts.Count == 0 && !env.Revive.CaptureRevive().IsSuccess;
            Require(Evidence.pendingWithoutProofPreserved && Evidence.noAutomaticGrant, "pending.no_proof"); Stage("no_proof_no_grant");
            Check(await env.Revive.AbandonPendingAsync(Wait));
            Evidence.explicitAbandon = Read().Pending == null && Read().Coins == 0 && Read().Claims.Single().State == ReviveClaimState.Aborted;
            Require(Evidence.explicitAbandon, "pending.abandon"); Stage("explicit_abandon");
            var task = Value(env.Revive.CaptureRevive()).WaitAsync(Wait).Preserve();
            for (int i = 0; i < 600 && !(env.Backend.Attempts.Count == 1 && env.Backend.Attempts[0].ShowCount == 1); i++) await UniTask.Yield();
            Require(env.Backend.Attempts.Count == 1 && env.Backend.Attempts[0].ShowCount == 1, "mock.show");
            var a = env.Backend.Attempts.Single(); Require(a.Publish(AdCallbackKind.Started), "mock.started");
            for (int i = 0; i < 10; i++) { Require(a.Publish(AdCallbackKind.Completed), "mock.completed"); Evidence.completedCallbacks++; }
            Require(a.Publish(AdCallbackKind.Closed) && a.CompleteShow(), "mock.closed"); Require(Value(await task).Granted, "mock.grant");
            Require(Read().Coins == 5 && Read().Pending == null && Read().Claims.Count(c => c.State == ReviveClaimState.Granted) == 1 && Read().Claims.Count == 2, "grant.once"); Stage("duplicate_callbacks_one_grant");
            await NoPending(); Evidence.checkpoint = 2;
        }
        async UniTask VerifyGranted()
        {
            var s = env.Store.Snapshot;
            Require(s.Coins == 5 && s.Pending == null && s.Claims.Count == 2 && s.Claims.Count(c => c.State == ReviveClaimState.Aborted) == 1 &&
                s.Claims.Count(c => c.State == ReviveClaimState.Granted) == 1 && s.Claims.Single(c => c.State == ReviveClaimState.Granted).Owner.SessionGeneration == 2, "grant.cross_runtime_restore"); Stage("granted_restored");
            await NoPending(); Require(env.Backend.Attempts.Count == 0 && Read().Coins == 5, "grant.no_redisplay");
            Evidence.noAutomaticGrant = true; Evidence.chainComplete = true; Evidence.checkpoint = 3;
        }
        async UniTask NoPending()
        {
            var before = StorageFormat.Digest(Raw()); int writes = files.Writes;
            var r = await env.Revive.RecoverPendingAsync(Wait);
            Evidence.duplicateRecoveryUnchanged = !r.IsSuccess && r.Error.Code == "revive.recovery.no_pending" && files.Writes == writes && StorageFormat.Digest(Raw()) == before;
            Require(Evidence.duplicateRecoveryUnchanged, "grant.duplicate_recovery"); Stage("duplicate_recovery_no_write");
        }
        void Count()
        {
            Evidence.modules = env?.Context.RetainedModuleCount ?? 0; Evidence.sessions = env?.Provider.OpenSessionCount ?? 0;
            Evidence.adsPending = env?.Ads.PendingCount ?? 0; Evidence.factsPending = env?.Facts.PendingCount ?? 0;
            Evidence.platformQueued = env?.Platform.QueuedCount ?? 0; Evidence.platformListeners = env?.Platform.ReadDiagnostics().Listeners ?? 0;
            Evidence.adAttempts = env?.Backend.Attempts.Count ?? 0;
        }
        public async UniTask Abort()
        {
            if (env == null) return;
            foreach (var a in env.Backend.Attempts) { a.Publish(AdCallbackKind.Closed); a.CompleteLoad(); a.CompleteShow(); a.CompleteCleanup(); }
            var r = await env.Context.CloseAsync(Wait); if (!r.IsSuccess) r = await env.Context.RetryCleanupAsync(Cleanup, Wait); Check(r); Count();
        }
    }
}
