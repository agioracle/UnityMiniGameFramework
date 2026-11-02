using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Storage;
using MiniFramework.Providers.WeChat.Storage;
using MiniFramework.WeChatStorage.TestSupport;
using NUnit.Framework;
using UnityEngine.TestTools;

public sealed class WeChatStorageServiceTests
{
    static readonly StorageAddress Address = new StorageAddress("local", "main");
    static OperationContext Wait => OperationContext.Unlimited();
    static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
    static string Key(int bank) => WeChatStorageProvider.KeyFor(Address, bank);
    static byte[] Bytes(int schema = 1, bool optional = false)
    {
        var rows = new List<StorageRecord> { new StorageRecord("progress", schema, "bytes-v" + schema, true, schema == 1 ? new byte[] { 7 } : new byte[] { 7, 9 }) };
        if (optional) rows.Add(new StorageRecord("addon", 9, "opaque", false, new byte[] { 201, 202 }));
        return StorageFormat.Encode(new StorageContainer(Address, 4, rows), new StorageLimits()).Value;
    }
    // Test-only scheduling decorator. Gates actual completion around the real dual-slot
    // provider to exercise coordinator ownership; the production file port never yields.
    sealed class Scheduled : IStorageProvider
    {
        internal readonly WeChatStorageProvider Inner;
        internal readonly UniTaskCompletionSource<bool> ReadGate = new UniTaskCompletionSource<bool>(), WriteGate = new UniTaskCompletionSource<bool>();
        internal bool HoldReads, HoldWrites, FailCleanup;
        internal int CleanupCount;
        internal Scheduled(WeChatStorageProvider inner) { Inner = inner; }
        public string Id => Inner.Id;
        public StorageCapabilities Capabilities => Inner.Capabilities;
        public IStorageSession Create(StorageAddress address) => new Session(this, Inner.Create(address));
        internal void Release() { ReadGate.TrySetResult(true); WriteGate.TrySetResult(true); }
        sealed class Session : IStorageSession
        {
            readonly Scheduled owner; readonly IStorageSession inner;
            internal Session(Scheduled owner, IStorageSession inner) { this.owner = owner; this.inner = inner; }
            public async UniTask<Result<StorageReadResult>> ReadAsync() { if (owner.HoldReads) await owner.ReadGate.Task; return await inner.ReadAsync(); }
            public async UniTask<StorageCommitReceipt> CommitAsync(StorageWriteRequest request) { if (owner.HoldWrites) await owner.WriteGate.Task; return await inner.CommitAsync(request); }
            public UniTask<Result> CleanupAsync(CleanupContext cleanup)
            { owner.CleanupCount++; return owner.FailCleanup ? UniTask.FromResult(Result.Failure(new FrameworkError(ErrorKind.CleanupFailed, "test.cleanup"))) : inner.CleanupAsync(cleanup); }
        }
    }
    sealed class Env
    {
        internal readonly WeChatTestFiles Files;
        internal readonly WeChatStorageProvider Provider;
        internal readonly Scheduled Adapter;
        internal readonly StorageService Service;
        internal readonly GameContext Context;
        internal byte Value = 1;
        internal Env(WeChatTestFiles files, int schema, bool holdRead, bool holdWrite)
        {
            Files = files ?? new WeChatTestFiles(); Provider = new WeChatStorageProvider(Files);
            Adapter = new Scheduled(Provider) { HoldReads = holdRead, HoldWrites = holdWrite };
            var registry = new StorageRegistry(); registry.Register(new StorageModuleRegistration("progress", schema, "bytes-v" + schema, true, typeof(byte[]),
                () => Result<byte[]>.Success(schema == 1 ? new[] { Value } : new byte[] { Value, 9 }),
                b => b.Length == schema ? Result.Success() : Result.Failure(new FrameworkError(ErrorKind.Integrity, "test.snapshot"))));
            var migrations = new StorageMigrationRegistry();
            migrations.Register(new StorageMigrationStep("progress", 1, "bytes-v1", "bytes-v2", b => Result<byte[]>.Success(b.Concat(new byte[] { 9 }).ToArray())));
            Service = new StorageService(Adapter, Address, registry, new StorageLimits(32, 65536, 65536), recovery: new StorageRecovery(migrations));
            Context = new GameContext(new[] { new ModuleRegistration("storage", Array.Empty<string>(), () => Service, typeof(IStorageRecoveryService)) }, Cleanup);
        }
        internal async UniTask Start() { Check(await Context.StartAsync(Wait)); }
        internal UniTask<Result<StorageCommitReceipt>> Save() => Service.Capture().Value.WaitAsync(Wait);
        internal async UniTask Close() { Check(await Context.CloseAsync(Wait)); Assert.AreEqual(0, Provider.OpenSessionCount); Assert.AreEqual(0, Context.RetainedModuleCount); }
    }
    readonly List<Env> envs = new List<Env>();
    Env New(WeChatTestFiles files = null, int schema = 1, bool holdRead = false, bool holdWrite = false) { var e = new Env(files, schema, holdRead, holdWrite); envs.Add(e); return e; }
    static WeChatTestFiles Saved(bool damaged = false, bool optional = false) { var f = new WeChatTestFiles(); f.Data[Key(0)] = Bytes(optional: optional); if (damaged) f.Data[Key(1)] = new byte[] { 1, 2, 3 }; return f; }
    static void Check(Result result) => Assert.IsTrue(result.IsSuccess, result.IsSuccess ? "" : result.Error.Code);
    static async UniTask Frames() { for (int i = 0; i < 3; i++) await UniTask.Yield(); }
    [UnityTearDown] public IEnumerator TearDown() => UniTask.ToCoroutine(async () =>
    {
        foreach (var e in envs)
        {
            e.Adapter.FailCleanup = false; e.Adapter.Release(); await Frames();
            var closed = await e.Context.CloseAsync(Wait); if (!closed.IsSuccess) Check(await e.Context.RetryCleanupAsync(Cleanup, Wait));
            Assert.AreEqual(0, e.Provider.OpenSessionCount); Assert.AreEqual(0, e.Context.RetainedModuleCount); Assert.AreEqual(0, e.Service.PendingCount);
        }
        envs.Clear();
    });

    [UnityTest] public IEnumerator NewSlotPublishesWithoutWriting() => UniTask.ToCoroutine(async () => { var e = New(); await e.Start(); Assert.IsTrue(e.Service.Restored.IsNew); Assert.AreEqual(0, e.Files.WriteKeys.Count); });
    [UnityTest] public IEnumerator SaveAndNewContextRestoreRealDualSlotBytes() => UniTask.ToCoroutine(async () => { var e = New(); await e.Start(); e.Value = 42; Assert.IsTrue((await e.Save()).IsSuccess); await e.Close(); var next = New(e.Files); await next.Start(); Assert.AreEqual(42, next.Service.Restored.Read("progress").Value[0]); Assert.AreEqual(1, next.Service.CommittedSequence); });
    [UnityTest] public IEnumerator CompatibleValidCopyPublishesButDamagedSlotBlocksSave() => UniTask.ToCoroutine(async () => { var e = New(Saved(true)); await e.Start(); Assert.AreEqual(7, e.Service.Restored.Read("progress").Value[0]); Assert.AreEqual("wechat.storage.repair_required", (await e.Save()).Error.Code); Assert.AreEqual(4, e.Service.CommittedSequence); Assert.AreEqual(StorageState.Ready, e.Service.State); Assert.AreEqual(0, e.Files.WriteKeys.Count); });
    [UnityTest] public IEnumerator CloseRepairReopenThenSaveSucceeds() => UniTask.ToCoroutine(async () => { var e = New(Saved(true)); await e.Start(); Assert.IsFalse((await e.Save()).IsSuccess); await e.Close(); var plan = (await e.Provider.InspectRepairAsync(Address)).Value; Assert.AreEqual(WeChatRepairDisposition.Repaired, (await e.Provider.RepairAsync(plan)).Disposition); var next = New(e.Files); await next.Start(); next.Value = 8; Assert.IsTrue((await next.Save()).IsSuccess); Assert.AreEqual(5, next.Service.CommittedSequence); CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, next.Files.Data[plan.BackupKey]); });
    [UnityTest] public IEnumerator BothBadFailStartupWithoutPublicationOrWrites() => UniTask.ToCoroutine(async () => { var f = Saved(true); f.Data[Key(0)] = new byte[] { 9 }; var e = New(f); Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess); Assert.IsNull(e.Service.Restored); Assert.AreEqual(0, f.WriteKeys.Count); });
    [UnityTest] public IEnumerator MigrationAgainstDamagedBankFailsBeforePublication() => UniTask.ToCoroutine(async () => { var e = New(Saved(true), schema: 2); Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess); Assert.IsNull(e.Service.Restored); Assert.AreEqual(0, e.Files.WriteKeys.Count); Assert.AreEqual("wechat.storage.repair_required", e.Service.LastReceipt.Error.Value.Code); });
    [UnityTest] public IEnumerator ExplicitRepairThenMigrationCommitsOnceBeforePublication() => UniTask.ToCoroutine(async () => { var e = New(Saved(true), schema: 2); await e.Context.StartAsync(Wait); await e.Close(); var plan = (await e.Provider.InspectRepairAsync(Address)).Value; await e.Provider.RepairAsync(plan); e.Files.WriteKeys.Clear(); var next = New(e.Files, schema: 2); await next.Start(); Assert.AreEqual(5, next.Service.Restored.Sequence); CollectionAssert.AreEqual(new byte[] { 7, 9 }, next.Service.Restored.Read("progress").Value); Assert.AreEqual(1, e.Files.WriteKeys.Count); });
    [UnityTest] public IEnumerator OptionalBytesSurviveRepairMigrationAndNextSave() => UniTask.ToCoroutine(async () => { var f = Saved(true, true); var p = new WeChatStorageProvider(f); await p.RepairAsync((await p.InspectRepairAsync(Address)).Value); var e = New(f, schema: 2); await e.Start(); await e.Save(); CollectionAssert.AreEqual(new byte[] { 201, 202 }, e.Service.Restored.Read("addon").Value); var s = e.Provider.Create(Address); var c = StorageFormat.Decode((await s.ReadAsync()).Value.CopyBytes(), new StorageLimits()).Value; CollectionAssert.AreEqual(new byte[] { 201, 202 }, c.Records.Single(r => r.ModuleId == "addon").CopyPayload()); await s.CleanupAsync(Cleanup); });
    [UnityTest] public IEnumerator CanceledColdCaptureDoesNotWrite() => UniTask.ToCoroutine(async () => { var e = New(); await e.Start(); var op = e.Service.Capture().Value; using (var c = new CancellationTokenSource()) { c.Cancel(); Assert.AreEqual(ErrorKind.Canceled, (await op.WaitAsync(OperationContext.Unlimited(c.Token))).Error.Kind); } Assert.AreEqual(0, e.Files.WriteKeys.Count); Assert.AreEqual(0, e.Service.PendingCount); });
    [UnityTest] public IEnumerator ZeroDeadlineColdCaptureDoesNotWrite() => UniTask.ToCoroutine(async () => { var e = New(); await e.Start(); var op = e.Service.Capture().Value; Assert.AreEqual(ErrorKind.Timeout, (await op.WaitAsync(default)).Error.Kind); Assert.AreEqual(0, e.Files.WriteKeys.Count); });
    [UnityTest] public IEnumerator CanceledAcceptedWaitDoesNotCancelActualDualSlotCommit() => UniTask.ToCoroutine(async () => { var e = New(Saved(), holdWrite: true); await e.Start(); e.Value = 8; var op = e.Service.Capture().Value; using (var c = new CancellationTokenSource()) { var observed = op.WaitAsync(OperationContext.Unlimited(c.Token)); c.Cancel(); Assert.AreEqual(ErrorKind.Canceled, (await observed).Error.Kind); Assert.IsFalse(op.CommitResult.HasReceipt); e.Adapter.Release(); await Frames(); Assert.IsTrue(op.CommitResult.IsCommitted); Assert.IsFalse(op.CommitResult.IsRestartRecoverable); Assert.AreEqual(5, e.Service.CommittedSequence); } });
    [UnityTest] public IEnumerator TimedOutAcceptedWaitStillRecordsLateActualReceipt() => UniTask.ToCoroutine(async () => { var e = New(Saved(), holdWrite: true); await e.Start(); var op = e.Service.Capture().Value; Assert.AreEqual(ErrorKind.Timeout, (await op.WaitAsync(new OperationContext(default, TimeSpan.FromMilliseconds(30)))).Error.Kind); Assert.IsFalse(op.CommitResult.HasReceipt); e.Adapter.Release(); await Frames(); Assert.IsTrue(op.CommitResult.IsCommitted); Assert.AreEqual(0, e.Service.PendingCount); });
    [UnityTest] public IEnumerator CancelInsideSynchronousWriteCannotUndoCommittedBytes() => UniTask.ToCoroutine(async () => { var e = New(Saved()); await e.Start(); using (var c = new CancellationTokenSource()) { e.Files.OnWrite = (k, b) => c.Cancel(); var op = e.Service.Capture().Value; Assert.AreEqual(ErrorKind.Canceled, (await op.WaitAsync(OperationContext.Unlimited(c.Token))).Error.Kind); Assert.IsTrue(op.CommitResult.IsCommitted); Assert.AreEqual(5, e.Service.CommittedSequence); } });
    [UnityTest] public IEnumerator CloseWaitsForOwnedWriteBeforeSessionCleanup() => UniTask.ToCoroutine(async () => { var e = New(Saved(), holdWrite: true); await e.Start(); var saving = e.Save(); var close = e.Context.CloseAsync(Wait); await Frames(); Assert.AreEqual(0, e.Adapter.CleanupCount); Assert.AreEqual(1, e.Provider.OpenSessionCount); Assert.IsNull(e.Service.Restored); e.Adapter.Release(); Check(await close); Assert.IsTrue((await saving).IsSuccess); Assert.AreEqual(0, e.Provider.OpenSessionCount); });
    [UnityTest] public IEnumerator CloseCancelsUnobservedCapture() => UniTask.ToCoroutine(async () => { var e = New(); await e.Start(); var op = e.Service.Capture().Value; await e.Close(); Assert.AreEqual(StorageCommitState.Canceled, op.State); Assert.AreEqual(0, e.Files.WriteKeys.Count); Assert.IsNull(e.Service.Restored); });
    [UnityTest] public IEnumerator CancelStartupPreventsLateReadMigrationAndPublication() => UniTask.ToCoroutine(async () => { var e = New(Saved(), schema: 2, holdRead: true); using (var c = new CancellationTokenSource()) { var start = e.Context.StartAsync(OperationContext.Unlimited(c.Token)); await Frames(); c.Cancel(); Assert.AreEqual(ErrorKind.Canceled, (await start).Error.Kind); e.Adapter.Release(); await e.Close(); Assert.IsNull(e.Service.Restored); Assert.AreEqual(0, e.Files.WriteKeys.Count); } });
    [UnityTest] public IEnumerator TimeoutStartupPreventsLateReadMigrationAndPublication() => UniTask.ToCoroutine(async () => { var e = New(Saved(), schema: 2, holdRead: true); Assert.AreEqual(ErrorKind.Timeout, (await e.Context.StartAsync(new OperationContext(default, TimeSpan.FromMilliseconds(30)))).Error.Kind); e.Adapter.Release(); await e.Close(); Assert.IsNull(e.Service.Restored); Assert.AreEqual(0, e.Files.WriteKeys.Count); });
    [UnityTest] public IEnumerator CleanupFailureRetainsSessionUntilExplicitRetry() => UniTask.ToCoroutine(async () => { var e = New(Saved()); await e.Start(); e.Adapter.FailCleanup = true; Assert.IsFalse((await e.Context.CloseAsync(Wait)).IsSuccess); Assert.AreEqual(1, e.Provider.OpenSessionCount); Assert.AreEqual("wechat.storage.repair.sessions_open", (await e.Provider.InspectRepairAsync(Address)).Error.Code); e.Adapter.FailCleanup = false; Check(await e.Context.RetryCleanupAsync(Cleanup, Wait)); Assert.AreEqual(0, e.Provider.OpenSessionCount); Assert.AreEqual(2, e.Adapter.CleanupCount); });
    [UnityTest] public IEnumerator QueuedWritesUseLatestSequenceAndFrozenSnapshots() => UniTask.ToCoroutine(async () => { var e = New(Saved(), holdWrite: true); await e.Start(); e.Value = 8; var a = e.Service.Capture().Value.WaitAsync(Wait); e.Value = 9; var b = e.Service.Capture().Value.WaitAsync(Wait); e.Value = 10; e.Adapter.Release(); Assert.AreEqual(5, (await a).Value.Sequence); Assert.AreEqual(6, (await b).Value.Sequence); await e.Close(); var next = New(e.Files); await next.Start(); Assert.AreEqual(9, next.Service.Restored.Read("progress").Value[0]); });
    [UnityTest] public IEnumerator UnknownFirstCommitRejectsQueuedSecondWithoutReplay() => UniTask.ToCoroutine(async () => { var e = New(Saved(), holdWrite: true); await e.Start(); e.Files.FaultKey = Key(1); e.Files.Next = FileFault.ThrowAfter; var a = e.Service.Capture().Value.WaitAsync(Wait); var b = e.Service.Capture().Value.WaitAsync(Wait); e.Adapter.Release(); Assert.IsFalse((await a).IsSuccess); Assert.AreEqual("storage.recovery_required", (await b).Error.Code); Assert.AreEqual(1, e.Files.WriteKeys.Count); Assert.AreEqual(0, e.Service.PendingCount); Assert.AreEqual(4, e.Service.CommittedSequence); });
    [UnityTest] public IEnumerator LateFailureDuringCloseDrainsWithoutPublishingAgain() => UniTask.ToCoroutine(async () => { var e = New(Saved(), holdWrite: true); await e.Start(); e.Files.FaultKey = Key(1); e.Files.Next = FileFault.ThrowAfter; var save = e.Save(); var close = e.Context.CloseAsync(Wait); e.Adapter.Release(); Check(await close); Assert.IsFalse((await save).IsSuccess); Assert.IsNull(e.Service.Restored); Assert.AreEqual(StorageState.Closed, e.Service.State); Assert.AreEqual(0, e.Provider.OpenSessionCount); });

    IEnumerator Unknown(FileFault fault) => UniTask.ToCoroutine(async () =>
    {
        var e = New(Saved()); await e.Start(); e.Value = 8; e.Files.FaultKey = Key(1); e.Files.Next = fault;
        var op = e.Service.Capture().Value; Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);
        Assert.AreEqual(StorageState.RecoveryRequired, e.Service.State); Assert.IsTrue(op.CommitResult.RequiresReconciliation);
        Assert.IsFalse(e.Service.Capture().IsSuccess); Assert.AreEqual(4, e.Service.CommittedSequence); Assert.IsNotNull(e.Service.CopyFailedCommit());
        CollectionAssert.AreEqual(Bytes(), e.Files.Data[Key(0)]); Assert.AreEqual(1, e.Files.WriteKeys.Count);
        await e.Close(); var next = New(e.Files); await next.Start();
        bool landed = fault == FileFault.ThrowAfter || fault == FileFault.VerifyRead;
        Assert.AreEqual(landed ? 5 : 4, next.Service.CommittedSequence); Assert.AreEqual(landed ? 8 : 7, next.Service.Restored.Read("progress").Value[0]);
    });
    [UnityTest] public IEnumerator ThrowBeforeWriteRequiresReconciliation() => Unknown(FileFault.ThrowBefore);
    [UnityTest] public IEnumerator ThrowAfterWriteReopensActualNewSequence() => Unknown(FileFault.ThrowAfter);
    [UnityTest] public IEnumerator TornWriteReopensOldValidSequence() => Unknown(FileFault.Torn);
    [UnityTest] public IEnumerator CorruptWriteReopensOldValidSequence() => Unknown(FileFault.Corrupt);
    [UnityTest] public IEnumerator DroppedWriteCannotReportPersistentSuccess() => Unknown(FileFault.Drop);
    [UnityTest] public IEnumerator FailedReadBackReconcilesActualWrittenSequence() => Unknown(FileFault.VerifyRead);
    [UnityTest] public IEnumerator QuotaFailureIsUnknownAfterWritePortEntry() => Unknown(FileFault.Quota);
}
