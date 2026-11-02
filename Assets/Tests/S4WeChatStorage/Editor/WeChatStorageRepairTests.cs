using System;
using System.IO;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Storage;
using MiniFramework.Providers.WeChat.Storage;
using MiniFramework.WeChatStorage.TestSupport;
using NUnit.Framework;

public sealed class WeChatStorageRepairTests
{
    static readonly StorageAddress Address = new StorageAddress("local", "main");
    static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
    WeChatTestFiles files; WeChatStorageProvider provider;
    static byte[] Good(long sequence = 4, byte value = 7, StorageAddress? address = null) => StorageFormat.Encode(
        new StorageContainer(address ?? Address, sequence, new[] { new StorageRecord("progress", 1, "bytes-v1", true, new[] { value }) }), new StorageLimits()).Value;
    string Key(int bank) => WeChatStorageProvider.KeyFor(Address, bank);
    [SetUp] public void Setup() { files = new WeChatTestFiles(); provider = new WeChatStorageProvider(files); Seed(0); }
    void Seed(int bad) { files.Data.Clear(); files.Data[Key(bad)] = new byte[] { 9, 8, 7 }; files.Data[Key(1 - bad)] = Good(); }
    static T Now<T>(UniTask<T> t) { Assert.IsTrue(t.Status.IsCompleted()); return t.GetAwaiter().GetResult(); }
    WeChatStorageRepairPlan Plan() { var p = Now(provider.InspectRepairAsync(Address)); Assert.IsTrue(p.IsSuccess, p.IsSuccess ? "" : p.Error.Code); return p.Value; }
    WeChatStorageRepairReceipt Repair() => Now(provider.RepairAsync(Plan()));
    void SameBanks(byte[] bad = null) { CollectionAssert.AreEqual(bad ?? new byte[] { 9, 8, 7 }, files.Data[Key(0)]); CollectionAssert.AreEqual(Good(), files.Data[Key(1)]); }

    [Test] public void InspectionHasNoWritesAndReturnsFrozenIdentity() { var p = Plan(); Assert.AreEqual(0, files.WriteKeys.Count); Assert.AreEqual(0, p.DamagedBank); Assert.AreEqual(4, p.Sequence); Assert.AreEqual(3, p.DamagedByteCount); Assert.AreEqual(StorageFormat.Digest(Good()), p.GoodDigest); }
    [Test] public void HealthyBanksNeverTriggerRepair() { files.Data[Key(0)] = Good(3); Assert.AreEqual("wechat.storage.repair.not_required", Now(provider.InspectRepairAsync(Address)).Error.Code); Assert.AreEqual(0, files.WriteKeys.Count); }
    [Test] public void MissingBankIsNotInventedAsCorruption() { files.Data.Remove(Key(0)); Assert.AreEqual("wechat.storage.repair.not_required", Now(provider.InspectRepairAsync(Address)).Error.Code); }
    [Test] public void BothBadCannotManufactureValidData() { files.Data[Key(1)] = new byte[] { 3 }; Assert.AreEqual("wechat.storage.repair.no_valid_copy", Now(provider.InspectRepairAsync(Address)).Error.Code); Assert.AreEqual(0, files.WriteKeys.Count); }
    [Test] public void OnlyBadCannotBecomeNewSave() { files.Data.Remove(Key(1)); Assert.IsFalse(Now(provider.InspectRepairAsync(Address)).IsSuccess); Assert.AreEqual(0, files.WriteKeys.Count); }
    [Test] public void FutureVersionIsNeverOfferedForRepair() { var b = Good(); b[8] = 2; using (var sha = System.Security.Cryptography.SHA256.Create()) { var hash = sha.ComputeHash(b.Take(b.Length - 32).ToArray()); Buffer.BlockCopy(hash, 0, b, b.Length - 32, 32); } files.Data[Key(0)] = b; Assert.AreEqual("storage.format.unsupported", Now(provider.InspectRepairAsync(Address)).Error.Code); }
    [Test] public void ForeignAddressIsNeverOfferedForRepair() { files.Data[Key(1)] = Good(address: new StorageAddress("other", "main")); Assert.AreEqual("wechat.storage.address", Now(provider.InspectRepairAsync(Address)).Error.Code); }
    [Test] public void SplitBrainRequiresSeparateDecision() { files.Data[Key(0)] = Good(value: 8); Assert.AreEqual("wechat.storage.split_brain", Now(provider.InspectRepairAsync(Address)).Error.Code); }
    [Test] public void InspectionRequiresClosedSessions() { var s = provider.Create(Address); Assert.AreEqual("wechat.storage.repair.sessions_open", Now(provider.InspectRepairAsync(Address)).Error.Code); Now(s.CleanupAsync(Cleanup)); Now(s.CleanupAsync(Cleanup)); Assert.AreEqual(0, provider.OpenSessionCount); Assert.IsNotNull(Plan()); }
    [Test] public void SessionOpenedAfterInspectionBlocksMutation() { var p = Plan(); var s = provider.Create(Address); Assert.AreEqual(WeChatRepairDisposition.Rejected, Now(provider.RepairAsync(p)).Disposition); Assert.AreEqual(0, files.WriteKeys.Count); Now(s.CleanupAsync(Cleanup)); }
    [Test] public void PlanCannotCrossProviderRoots() { var p = Plan(); var other = new WeChatStorageProvider(files); Assert.AreEqual("wechat.storage.repair.plan", Now(other.RepairAsync(p)).Error.Value.Code); Assert.AreEqual(0, files.WriteKeys.Count); }
    [Test] public void NullPlanFailsWithoutIo() { int reads = files.Reads; Assert.AreEqual(WeChatRepairDisposition.Rejected, Now(provider.RepairAsync(null)).Disposition); Assert.AreEqual(reads, files.Reads); }
    [Test] public void ChangedValidBankRejectsStalePlan() { var p = Plan(); files.Data[Key(1)] = Good(5); Assert.AreEqual("wechat.storage.repair.conflict", Now(provider.RepairAsync(p)).Error.Value.Code); Assert.AreEqual(0, files.WriteKeys.Count); }
    [Test] public void ChangedBadBankRejectsStalePlan() { var p = Plan(); files.Data[Key(0)] = new byte[] { 1 }; Assert.AreEqual("wechat.storage.repair.conflict", Now(provider.RepairAsync(p)).Error.Value.Code); Assert.AreEqual(0, files.WriteKeys.Count); }
    [Test] public void SwappedBanksRejectStalePlan() { var p = Plan(); Seed(1); Assert.AreEqual("wechat.storage.repair.conflict", Now(provider.RepairAsync(p)).Error.Value.Code); Assert.AreEqual(0, files.WriteKeys.Count); }
    [Test] public void ExistingExactBackupIsReusedWithoutOverwriting() { var p = Plan(); files.Data[p.BackupKey] = new byte[] { 9, 8, 7 }; Assert.AreEqual(WeChatRepairDisposition.Repaired, Now(provider.RepairAsync(p)).Disposition); CollectionAssert.AreEqual(new[] { Key(0) }, files.WriteKeys); }
    [Test] public void ConflictingBackupIsNeverOverwritten() { var p = Plan(); files.Data[p.BackupKey] = new byte[] { 1 }; Assert.AreEqual("wechat.storage.repair.backup_conflict", Now(provider.RepairAsync(p)).Error.Value.Code); SameBanks(); CollectionAssert.AreEqual(new byte[] { 1 }, files.Data[p.BackupKey]); Assert.AreEqual(0, files.WriteKeys.Count); }
    [Test] public void BackupReadDeniedDoesNotTouchBanks() { var p = Plan(); files.DeniedKey = p.BackupKey; Assert.AreEqual(WeChatRepairDisposition.Rejected, Now(provider.RepairAsync(p)).Disposition); SameBanks(); Assert.AreEqual(0, files.WriteKeys.Count); }
    [Test] public void BankReadDeniedIsNotMissing() { files.DeniedKey = Key(0); Assert.AreEqual(ErrorKind.PermissionDenied, Now(provider.InspectRepairAsync(Address)).Error.Kind); Assert.AreEqual(0, files.WriteKeys.Count); }

    [TestCase(FileFault.ThrowBefore)] [TestCase(FileFault.ThrowAfter)] [TestCase(FileFault.Torn)] [TestCase(FileFault.Corrupt)] [TestCase(FileFault.Drop)] [TestCase(FileFault.VerifyRead)] [TestCase(FileFault.Quota)]
    public void BackupFailureNeverStartsBankRepair(FileFault fault) { var p = Plan(); files.FaultKey = p.BackupKey; files.Next = fault; var r = Now(provider.RepairAsync(p)); Assert.AreEqual(WeChatRepairDisposition.BackupIndeterminate, r.Disposition); Assert.IsFalse(r.BackupVerified); SameBanks(); CollectionAssert.AreEqual(new[] { p.BackupKey }, files.WriteKeys); }

    [TestCase(FileFault.ThrowBefore)] [TestCase(FileFault.ThrowAfter)] [TestCase(FileFault.Torn)] [TestCase(FileFault.Corrupt)] [TestCase(FileFault.Drop)] [TestCase(FileFault.VerifyRead)] [TestCase(FileFault.Quota)]
    public void RepairFailureKeepsVerifiedBackupAndValidBank(FileFault fault) { var p = Plan(); files.FaultKey = Key(0); files.Next = fault; var r = Now(provider.RepairAsync(p)); Assert.AreEqual(WeChatRepairDisposition.RepairIndeterminate, r.Disposition); Assert.IsTrue(r.BackupVerified); CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, files.Data[p.BackupKey]); CollectionAssert.AreEqual(Good(), files.Data[Key(1)]); Assert.AreEqual(StoragePersistence.Volatile, r.Persistence); }

    [TestCase(0)] [TestCase(1)] public void SuccessfulRepairDuplicatesValidBytesWithoutAdvancingSequence(int bank) { Seed(bank); var p = Plan(); var r = Now(provider.RepairAsync(p)); Assert.AreEqual(WeChatRepairDisposition.Repaired, r.Disposition); Assert.IsTrue(r.BackupVerified); CollectionAssert.AreEqual(Good(), files.Data[Key(0)]); CollectionAssert.AreEqual(Good(), files.Data[Key(1)]); CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, files.Data[p.BackupKey]); CollectionAssert.AreEqual(new[] { p.BackupKey, Key(bank) }, files.WriteKeys); }
    [Test] public void EmptyDamagedFileIsPreservedExactly() { files.Data[Key(0)] = Array.Empty<byte>(); var p = Plan(); Assert.AreEqual(WeChatRepairDisposition.Repaired, Now(provider.RepairAsync(p)).Disposition); Assert.AreEqual(0, files.Data[p.BackupKey].Length); }
    [Test] public void ChangedBankDuringBackupIsRevalidatedBeforeRepair() { var p = Plan(); files.OnWrite = (k, b) => files.Data[Key(1)] = Good(5); var r = Now(provider.RepairAsync(p)); Assert.AreEqual(WeChatRepairDisposition.Rejected, r.Disposition); Assert.IsTrue(r.BackupVerified); Assert.AreEqual(1, files.WriteKeys.Count); CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, files.Data[Key(0)]); }
    [Test] public void ReentrantMaintenanceCannotReleaseOuterLease() { var p = Plan(); files.OnWrite = (k, b) => { Assert.AreEqual("wechat.storage.busy", Now(provider.InspectRepairAsync(Address)).Error.Code); Assert.Throws<InvalidOperationException>(() => provider.Create(Address)); }; Assert.AreEqual(WeChatRepairDisposition.Repaired, Now(provider.RepairAsync(p)).Disposition); }
    [Test] public void WorkerMaintenanceNeverTouchesFiles() { var p = Plan(); int reads = files.Reads; WeChatStorageRepairReceipt r = null; var t = new Thread(() => r = provider.RepairAsync(p).GetAwaiter().GetResult()); t.Start(); t.Join(); Assert.AreEqual("wechat.storage.thread", r.Error.Value.Code); Assert.AreEqual(reads, files.Reads); Assert.AreEqual(0, files.WriteKeys.Count); }
    [Test] public void SessionCountDoesNotUnderflowOnRepeatedCleanup() { var a = provider.Create(Address); var b = provider.Create(Address); Now(a.CleanupAsync(Cleanup)); Now(a.CleanupAsync(Cleanup)); Assert.AreEqual(1, provider.OpenSessionCount); Assert.IsFalse(Now(provider.InspectRepairAsync(Address)).IsSuccess); Now(b.CleanupAsync(Cleanup)); Assert.AreEqual(0, provider.OpenSessionCount); }
    [Test] public void PortCannotMutateRetainedRepairPlanThroughWriteArgument() { var p = Plan(); files.MutateArgument = true; Assert.AreEqual(WeChatRepairDisposition.Repaired, Now(provider.RepairAsync(p)).Disposition); CollectionAssert.AreEqual(Good(), files.Data[Key(0)]); CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, files.Data[p.BackupKey]); }
    [Test] public void ReusingSuccessfulPlanDoesNotReplayWrites() { var p = Plan(); Now(provider.RepairAsync(p)); int count = files.WriteKeys.Count; Assert.AreEqual(WeChatRepairDisposition.Rejected, Now(provider.RepairAsync(p)).Disposition); Assert.AreEqual(count, files.WriteKeys.Count); }
    [Test] public void ExplicitReinspectionAfterDroppedRepairReusesBackup() { var p = Plan(); files.FaultKey = Key(0); files.Next = FileFault.Drop; Now(provider.RepairAsync(p)); files.WriteKeys.Clear(); Assert.AreEqual(WeChatRepairDisposition.Repaired, Repair().Disposition); CollectionAssert.AreEqual(new[] { Key(0) }, files.WriteKeys); CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, files.Data[p.BackupKey]); }
    [Test] public void ExplicitReinspectionAfterTornRepairPreservesBothDamageVersions() { var p = Plan(); files.FaultKey = Key(0); files.Next = FileFault.Torn; Now(provider.RepairAsync(p)); var next = Plan(); Assert.AreNotEqual(p.BackupKey, next.BackupKey); Assert.AreEqual(WeChatRepairDisposition.Repaired, Now(provider.RepairAsync(next)).Disposition); CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, files.Data[p.BackupKey]); CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, files.Data[next.BackupKey]); }

    sealed class DiskFiles : IWeChatStorageFiles
    {
        readonly string root; public DiskFiles(string root) { this.root = root; }
        public StoragePersistence Persistence => StoragePersistence.Volatile;
        string PathFor(string key) => Path.Combine(root, StorageFormat.Digest(System.Text.Encoding.UTF8.GetBytes(key)));
        public Result<StorageReadResult> Read(string key, int maxBytes) { string p = PathFor(key); if (!File.Exists(p)) return Result<StorageReadResult>.Success(new StorageReadResult(false)); if (new FileInfo(p).Length > maxBytes) return Result<StorageReadResult>.Failure(new FrameworkError(ErrorKind.Integrity, "test.disk.size")); return Result<StorageReadResult>.Success(new StorageReadResult(true, File.ReadAllBytes(p))); }
        public Result Write(string key, byte[] bytes) { File.WriteAllBytes(PathFor(key), bytes); return Result.Success(); }
    }
    [Test] public void EditorDiskBackupSurvivesPortReconstructionWithExactBytes()
    {
        var root = Path.GetFullPath("Library/S409RepairTests/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try { var disk = new DiskFiles(root); disk.Write(Key(0), new byte[] { 9, 8, 7 }); disk.Write(Key(1), Good()); var p = new WeChatStorageProvider(disk); var plan = Now(p.InspectRepairAsync(Address)).Value; Assert.AreEqual(WeChatRepairDisposition.Repaired, Now(p.RepairAsync(plan)).Disposition); var reopened = new DiskFiles(root); CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, reopened.Read(plan.BackupKey, 65536).Value.CopyBytes()); CollectionAssert.AreEqual(Good(), reopened.Read(Key(0), 65536).Value.CopyBytes()); }
        finally { Directory.Delete(root, true); }
    }
}
