using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Storage;
namespace MiniFramework.Providers.WeChat.Storage
{
    // Synchronous, bounded, single host thread only. No caller cancellation or hidden retry.
    // A missing file is Success(Exists=false); permission/IO/oversize are failures, never missing.
    // Implementations must check length BEFORE allocating and copy write input before retaining it.
    public interface IWeChatStorageFiles
    {
        StoragePersistence Persistence { get; }
        Result<StorageReadResult> Read(string key, int maxBytes);
        Result Write(string key, byte[] bytes);
    }
    public sealed partial class WeChatStorageProvider : IStorageProvider
    {
        readonly IWeChatStorageFiles files;
        readonly StorageLimits limits;
        readonly int threadId;
        bool busy;
        int openSessions;
        public int OpenSessionCount => openSessions;
        public string Id => "wechat-storage-dual-v1";
        public StorageCapabilities Capabilities { get; }
        public WeChatStorageProvider(IWeChatStorageFiles files, int maxContainerBytes = 65536)
            : this(files,new WeChatStorageOptions(maxContainerBytes)) { }
        public WeChatStorageProvider(IWeChatStorageFiles files, WeChatStorageOptions options)
        {
            this.files = files ?? throw new ArgumentNullException(nameof(files));
            if(options==null)throw new ArgumentNullException(nameof(options));
            int maxContainerBytes=options.MaxContainerBytes;
            if(files is IWeChatStorageFileLimits bounded && bounded.MaxFileBytes<maxContainerBytes)
                throw new ArgumentException("Storage provider budget exceeds file port budget.",nameof(options));
            limits = new StorageLimits(1024, maxContainerBytes, maxContainerBytes);
            Capabilities = new StorageCapabilities(maxContainerBytes, files.Persistence, true);
            threadId = Thread.CurrentThread.ManagedThreadId;
        }
        public IStorageSession Create(StorageAddress address)
        {
            if (!address.IsValid) throw new ArgumentException(nameof(address));
            if (Thread.CurrentThread.ManagedThreadId != threadId || busy)
                throw new InvalidOperationException("Storage session creation requires the idle owner thread.");
            openSessions++;
            return new Session(this, address);
        }
        public static string KeyFor(StorageAddress address, int bank)
        {
            if (!address.IsValid || bank < 0 || bank > 1) throw new ArgumentException("Address/bank invalid.");
            return Hex(address.Namespace) + "-" + Hex(address.Slot) + "." + bank;
        }
        static string Hex(string value)
        {
            var b = Encoding.ASCII.GetBytes(value); var s = new StringBuilder(b.Length * 2);
            foreach (var x in b) s.Append(x.ToString("x2")); return s.ToString();
        }
        internal static FrameworkError Error(string code, ErrorKind kind = ErrorKind.Storage) => new FrameworkError(kind, code);
        FrameworkError? Enter()
        {
            if (Thread.CurrentThread.ManagedThreadId != threadId) return Error("wechat.storage.thread", ErrorKind.InvalidState);
            if (busy) return Error("wechat.storage.busy", ErrorKind.InvalidState);
            busy = true; return null;
        }
        sealed class Selection
        {
            public byte[] Bytes; public long Sequence; public int Bank; public bool Damaged;
        }
        Result<Selection> Select(StorageAddress address)
        {
            var selected = new Selection { Bank = 1 }; bool exists = false;
            for (int bank = 0; bank < 2; bank++)
            {
                var r = files.Read(KeyFor(address, bank), limits.MaxContainerBytes);
                if (!r.IsSuccess) return Result<Selection>.Failure(r.Error);
                if (r.Value == null) return Result<Selection>.Failure(Error("wechat.storage.read_null", ErrorKind.Provider));
                if (!r.Value.Exists) continue;
                exists = true; var bytes = r.Value.CopyBytes(); var decoded = StorageFormat.Decode(bytes, limits);
                if (!decoded.IsSuccess)
                {
                    // Never reinterpret a future format as an old/empty slot.
                    if (decoded.Error.Code == "storage.format.unsupported") return Result<Selection>.Failure(decoded.Error);
                    selected.Damaged = true; continue;
                }
                var c = decoded.Value;
                if (c.Address.Namespace != address.Namespace || c.Address.Slot != address.Slot)
                    return Result<Selection>.Failure(Error("wechat.storage.address", ErrorKind.Integrity));
                if (selected.Bytes != null && c.Sequence == selected.Sequence && StorageFormat.Digest(bytes) != StorageFormat.Digest(selected.Bytes))
                    return Result<Selection>.Failure(Error("wechat.storage.split_brain", ErrorKind.Integrity));
                if (selected.Bytes == null || c.Sequence > selected.Sequence)
                { selected.Bytes = bytes; selected.Sequence = c.Sequence; selected.Bank = bank; }
            }
            if (exists && selected.Bytes == null) return Result<Selection>.Failure(Error("wechat.storage.corrupt", ErrorKind.Integrity));
            return Result<Selection>.Success(selected);
        }
        sealed class Session : IStorageSession
        {
            readonly WeChatStorageProvider owner; readonly StorageAddress address;
            bool closed, unknown, active;
            internal Session(WeChatStorageProvider owner, StorageAddress address) { this.owner = owner; this.address = address; }
            FrameworkError? Begin()
            {
                if (closed || unknown) return Error(closed ? "wechat.storage.closed" : "wechat.storage.reconcile", ErrorKind.InvalidState);
                var error = owner.Enter(); if (!error.HasValue) active = true; return error;
            }
            void End() { active = false; owner.busy = false; }
            public UniTask<Result<StorageReadResult>> ReadAsync()
            {
                var error = Begin(); if (error.HasValue) return UniTask.FromResult(Result<StorageReadResult>.Failure(error.Value));
                try
                {
                    var r = owner.Select(address);
                    return UniTask.FromResult(r.IsSuccess ? Result<StorageReadResult>.Success(new StorageReadResult(r.Value.Bytes != null, r.Value.Bytes)) : Result<StorageReadResult>.Failure(r.Error));
                }
                catch (Exception) { return UniTask.FromResult(Result<StorageReadResult>.Failure(Error("wechat.storage.read", ErrorKind.Provider))); }
                finally { End(); }
            }
            public UniTask<StorageCommitReceipt> CommitAsync(StorageWriteRequest request)
            {
                var error = Begin(); if (error.HasValue) return UniTask.FromResult(StorageCommitReceipt.Rejected(error.Value));
                bool attempted = false;
                try
                {
                    var current = owner.Select(address);
                    if (!current.IsSuccess) return Done(StorageCommitReceipt.Rejected(current.Error));
                    // Preserve damaged bytes for explicit repair; normal commits never erase this evidence.
                    if (current.Value.Damaged) return Done(StorageCommitReceipt.Rejected(Error("wechat.storage.repair_required", ErrorKind.Integrity)));
                    var valid = StorageWriteValidation.Check(request, address, current.Value.Bytes, owner.limits);
                    if (!valid.IsSuccess) return Done(StorageCommitReceipt.Rejected(valid.Error));
                    var bytes = request.CopyBytes(); var key = KeyFor(address, 1 - current.Value.Bank);
                    attempted = true; var written = owner.files.Write(key, bytes);
                    if (!written.IsSuccess) return Unknown(written.Error);
                    var verified = owner.files.Read(key, owner.limits.MaxContainerBytes);
                    if (!verified.IsSuccess) return Unknown(verified.Error);
                    if (verified.Value == null || !verified.Value.Exists) return Unknown(Error("wechat.storage.verify_missing", ErrorKind.Integrity));
                    var stored = verified.Value.CopyBytes(); var check = StorageFormat.Decode(stored, owner.limits);
                    if (!check.IsSuccess || StorageFormat.Digest(stored) != request.Digest)
                        return Unknown(Error("wechat.storage.verify", ErrorKind.Integrity));
                    return Done(StorageCommitReceipt.Committed(request.Sequence, request.Digest, owner.Capabilities.Persistence));
                }
                catch (Exception) { return attempted ? Unknown(Error("wechat.storage.write_unknown", ErrorKind.Provider)) : Done(StorageCommitReceipt.Rejected(Error("wechat.storage.prewrite", ErrorKind.Provider))); }
                finally { End(); }
            }
            static UniTask<StorageCommitReceipt> Done(StorageCommitReceipt receipt) => UniTask.FromResult(receipt);
            UniTask<StorageCommitReceipt> Unknown(FrameworkError error) { unknown = true; return Done(StorageCommitReceipt.Indeterminate(error)); }
            public UniTask<Result> CleanupAsync(CleanupContext cleanup)
            {
                if (Thread.CurrentThread.ManagedThreadId != owner.threadId || active)
                    return UniTask.FromResult(Result.Failure(Error("wechat.storage.cleanup_busy", ErrorKind.CleanupFailed)));
                if (closed) return UniTask.FromResult(Result.Success());
                if (owner.files is IWeChatStorageCleanup pending && pending.CleanupPending)
                {
                    var released = pending.RetryCleanup();
                    if (!released.IsSuccess) return UniTask.FromResult(Result.Failure(Error("wechat.storage.files_pending", ErrorKind.CleanupFailed)));
                }
                if (!closed) { closed = true; owner.openSessions--; }
                return UniTask.FromResult(Result.Success());
            }
        }
    }
}
