using System;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Storage;

namespace MiniFramework.Providers.WeChat.Storage
{
    // Maintenance is explicit, synchronous and separate from business commit receipts.
    public enum WeChatRepairDisposition { Rejected, BackupIndeterminate, RepairIndeterminate, Repaired }

    public sealed class WeChatStorageRepairPlan
    {
        internal readonly WeChatStorageProvider Owner;
        readonly byte[] goodBytes, damagedBytes;
        public StorageAddress Address { get; }
        public int DamagedBank { get; }
        public long Sequence { get; }
        public string GoodDigest { get; }
        public string DamagedDigest { get; }
        public int DamagedByteCount => damagedBytes.Length;
        public string BackupKey { get; }
        internal byte[] CopyGood() => (byte[])goodBytes.Clone();
        internal byte[] CopyDamaged() => (byte[])damagedBytes.Clone();
        internal WeChatStorageRepairPlan(WeChatStorageProvider owner, StorageAddress address, int bank, long sequence, byte[] good, byte[] damaged)
        {
            Owner = owner; Address = address; DamagedBank = bank; Sequence = sequence;
            goodBytes = (byte[])good.Clone(); damagedBytes = (byte[])damaged.Clone();
            GoodDigest = StorageFormat.Digest(good); DamagedDigest = StorageFormat.Digest(damaged);
            BackupKey = WeChatStorageProvider.KeyFor(address, bank) + ".bad-" + DamagedDigest;
        }
    }

    public sealed class WeChatStorageRepairReceipt
    {
        public WeChatRepairDisposition Disposition { get; }
        public string BackupKey { get; }
        public bool BackupVerified { get; }
        public StoragePersistence Persistence { get; }
        public FrameworkError? Error { get; }
        internal WeChatStorageRepairReceipt(WeChatRepairDisposition disposition, string key, bool verified, StoragePersistence persistence, FrameworkError? error)
        { Disposition = disposition; BackupKey = key; BackupVerified = verified; Persistence = persistence; Error = error; }
    }

    public sealed partial class WeChatStorageProvider
    {
        FrameworkError? EnterMaintenance()
        {
            var error = Enter();
            if (error.HasValue) return error;
            if (openSessions == 0) return null;
            busy = false;
            return Error("wechat.storage.repair.sessions_open", ErrorKind.InvalidState);
        }

        Result<StorageReadResult> ReadMaintenance(string key)
        {
            var read = files.Read(key, limits.MaxContainerBytes);
            return read.IsSuccess && read.Value == null
                ? Result<StorageReadResult>.Failure(Error("wechat.storage.read_null", ErrorKind.Provider)) : read;
        }

        Result<WeChatStorageRepairPlan> Inspect(StorageAddress address)
        {
            var banks = new byte[2][]; int damaged = -1, good = -1; long sequence = 0;
            for (int bank = 0; bank < 2; bank++)
            {
                var read = ReadMaintenance(KeyFor(address, bank));
                if (!read.IsSuccess) return Result<WeChatStorageRepairPlan>.Failure(read.Error);
                if (!read.Value.Exists) continue;
                banks[bank] = read.Value.CopyBytes();
                var decoded = StorageFormat.Decode(banks[bank], limits);
                if (!decoded.IsSuccess)
                {
                    if (decoded.Error.Code == "storage.format.unsupported") return Result<WeChatStorageRepairPlan>.Failure(decoded.Error);
                    damaged = bank; continue;
                }
                if (decoded.Value.Address.Namespace != address.Namespace || decoded.Value.Address.Slot != address.Slot)
                    return Result<WeChatStorageRepairPlan>.Failure(Error("wechat.storage.address", ErrorKind.Integrity));
                if (good >= 0)
                {
                    bool conflict = decoded.Value.Sequence == sequence && StorageFormat.Digest(banks[bank]) != StorageFormat.Digest(banks[good]);
                    return Result<WeChatStorageRepairPlan>.Failure(Error(conflict
                        ? "wechat.storage.split_brain" : "wechat.storage.repair.not_required", conflict ? ErrorKind.Integrity : ErrorKind.InvalidState));
                }
                good = bank; sequence = decoded.Value.Sequence;
            }
            if (good < 0) return Result<WeChatStorageRepairPlan>.Failure(Error("wechat.storage.repair.no_valid_copy", ErrorKind.Integrity));
            if (damaged < 0) return Result<WeChatStorageRepairPlan>.Failure(Error("wechat.storage.repair.not_required", ErrorKind.InvalidState));
            return Result<WeChatStorageRepairPlan>.Success(new WeChatStorageRepairPlan(this, address, damaged, sequence, banks[good], banks[damaged]));
        }

        // Drain/close every service/session in this root before inspection and repair.
        // The caller must also exclude other provider instances / external writers.
        public UniTask<Result<WeChatStorageRepairPlan>> InspectRepairAsync(StorageAddress address)
        {
            if (!address.IsValid) return UniTask.FromResult(Result<WeChatStorageRepairPlan>.Failure(Error("wechat.storage.address", ErrorKind.InvalidArgument)));
            var error = EnterMaintenance();
            if (error.HasValue) return UniTask.FromResult(Result<WeChatStorageRepairPlan>.Failure(error.Value));
            try { return UniTask.FromResult(Inspect(address)); }
            catch (Exception) { return UniTask.FromResult(Result<WeChatStorageRepairPlan>.Failure(Error("wechat.storage.repair.inspect", ErrorKind.Provider))); }
            finally { busy = false; }
        }

        Result CheckPlan(WeChatStorageRepairPlan plan)
        {
            var current = Inspect(plan.Address);
            if (!current.IsSuccess) return Result.Failure(current.Error);
            var value = current.Value;
            return value.DamagedBank == plan.DamagedBank && value.Sequence == plan.Sequence && value.GoodDigest == plan.GoodDigest && value.DamagedDigest == plan.DamagedDigest
                ? Result.Success() : Result.Failure(Error("wechat.storage.repair.conflict", ErrorKind.InvalidState));
        }

        public UniTask<WeChatStorageRepairReceipt> RepairAsync(WeChatStorageRepairPlan plan)
        {
            WeChatStorageRepairReceipt Receipt(WeChatRepairDisposition status, bool verified, FrameworkError? error = null)
                => new WeChatStorageRepairReceipt(status, plan?.BackupKey, verified, Capabilities.Persistence, error);
            UniTask<WeChatStorageRepairReceipt> Rejected(FrameworkError error, bool verified = false)
                => UniTask.FromResult(Receipt(WeChatRepairDisposition.Rejected, verified, error));
            if (plan == null || !ReferenceEquals(plan.Owner, this)) return Rejected(Error("wechat.storage.repair.plan", ErrorKind.InvalidArgument));
            var enter = EnterMaintenance(); if (enter.HasValue) return Rejected(enter.Value);
            bool backupAttempted = false, backupVerified = false, bankAttempted = false;
            try
            {
                var current = CheckPlan(plan); if (!current.IsSuccess) return Rejected(current.Error);
                var backup = ReadMaintenance(plan.BackupKey);
                if (!backup.IsSuccess) return Rejected(backup.Error);
                if (backup.Value.Exists)
                {
                    if (StorageFormat.Digest(backup.Value.CopyBytes()) != plan.DamagedDigest)
                        return Rejected(Error("wechat.storage.repair.backup_conflict", ErrorKind.Integrity));
                }
                else
                {
                    backupAttempted = true;
                    var write = files.Write(plan.BackupKey, plan.CopyDamaged());
                    if (!write.IsSuccess) return UniTask.FromResult(Receipt(WeChatRepairDisposition.BackupIndeterminate, false, write.Error));
                    backup = ReadMaintenance(plan.BackupKey);
                    if (!backup.IsSuccess || !backup.Value.Exists || StorageFormat.Digest(backup.Value.CopyBytes()) != plan.DamagedDigest)
                        return UniTask.FromResult(Receipt(WeChatRepairDisposition.BackupIndeterminate, false,
                            !backup.IsSuccess ? backup.Error : Error("wechat.storage.repair.backup_verify", ErrorKind.Integrity)));
                }
                backupVerified = true;
                // A port callback or an out-of-contract external writer may have changed the banks.
                current = CheckPlan(plan); if (!current.IsSuccess) return Rejected(current.Error, true);
                bankAttempted = true;
                var repaired = files.Write(KeyFor(plan.Address, plan.DamagedBank), plan.CopyGood());
                if (!repaired.IsSuccess) return UniTask.FromResult(Receipt(WeChatRepairDisposition.RepairIndeterminate, true, repaired.Error));
                for (int bank = 0; bank < 2; bank++)
                {
                    var read = ReadMaintenance(KeyFor(plan.Address, bank));
                    if (!read.IsSuccess || !read.Value.Exists || StorageFormat.Digest(read.Value.CopyBytes()) != plan.GoodDigest)
                        return UniTask.FromResult(Receipt(WeChatRepairDisposition.RepairIndeterminate, true,
                            !read.IsSuccess ? read.Error : Error("wechat.storage.repair.verify", ErrorKind.Integrity)));
                }
                return UniTask.FromResult(Receipt(WeChatRepairDisposition.Repaired, true));
            }
            catch (Exception)
            {
                return UniTask.FromResult(Receipt(bankAttempted ? WeChatRepairDisposition.RepairIndeterminate : backupAttempted && !backupVerified
                    ? WeChatRepairDisposition.BackupIndeterminate : WeChatRepairDisposition.Rejected, backupVerified,
                    Error("wechat.storage.repair.exception", ErrorKind.Provider)));
            }
            finally { busy = false; }
        }
    }
}
