using System;
using System.Text;
using System.Threading;
using MiniFramework.Contracts;
using MiniFramework.Storage;

namespace MiniFramework.Providers.WeChat.Storage
{
    // Negative ABI results, shared with the owned WebGL bridge. Unknown values fail closed.
    public enum WeChatFileStatus { Missing = -1, Permission = -2, Quota = -3, TooLarge = -4,
        Invalid = -5, Unsupported = -6, Io = -7, Protocol = -8, Busy = -9 }
    public interface IWeChatFileTransport
    {
        // Nonnegative = exact bounded length; transport retains one owned read buffer.
        int BeginRead(string hashedKey, int maxBytes);
        int FinishRead(byte[] destination);
        void ReleaseRead();
        int Write(string hashedKey, byte[] bytes);
    }
    public sealed class WeChatNativeFilePort : IWeChatStorageFiles, IWeChatStorageFileLimits
    {
        // Legacy default retained for source compatibility; actual limit is per instance.
        public const int MaxBytes = WeChatStorageOptions.DefaultMaxContainerBytes;
        public int MaxFileBytes {get;}
        readonly IWeChatFileTransport transport;
        readonly int thread = Thread.CurrentThread.ManagedThreadId;
        bool busy;
        public StoragePersistence Persistence { get; }
        public WeChatNativeFilePort(IWeChatFileTransport transport, StoragePersistence persistence, WeChatStorageOptions options=null)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            if (!Enum.IsDefined(typeof(StoragePersistence), persistence)) throw new ArgumentOutOfRangeException(nameof(persistence));
            Persistence = persistence;
            MaxFileBytes=(options??new WeChatStorageOptions()).MaxContainerBytes;
        }
        public static Result<string> MapKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 512 || key.Contains("..")) return Result<string>.Failure(Error("key", ErrorKind.InvalidArgument));
            foreach (char c in key) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-'))
                return Result<string>.Failure(Error("key", ErrorKind.InvalidArgument));
            return Result<string>.Success(StorageFormat.Digest(Encoding.ASCII.GetBytes(key)));
        }
        static FrameworkError Error(string code, ErrorKind kind) => new FrameworkError(kind, "wechat.files." + code);
        public static FrameworkError Classify(int status)
        {
            switch ((WeChatFileStatus)status)
            {
                case WeChatFileStatus.Missing: return Error("missing", ErrorKind.Storage);
                case WeChatFileStatus.Permission: return Error("permission", ErrorKind.PermissionDenied);
                case WeChatFileStatus.Quota: return Error("quota", ErrorKind.Storage);
                case WeChatFileStatus.TooLarge: return Error("oversize", ErrorKind.Integrity);
                case WeChatFileStatus.Invalid: return Error("argument", ErrorKind.InvalidArgument);
                case WeChatFileStatus.Unsupported: return Error("unsupported", ErrorKind.NotSupported);
                case WeChatFileStatus.Io: return Error("io", ErrorKind.Provider);
                case WeChatFileStatus.Busy: return Error("busy", ErrorKind.InvalidState);
                default: return Error("protocol", ErrorKind.Integrity);
            }
        }
        FrameworkError? Enter()
        {
            if (Thread.CurrentThread.ManagedThreadId != thread) return Error("thread", ErrorKind.InvalidState);
            if (busy) return Error("busy", ErrorKind.InvalidState);
            busy = true; return null;
        }
        public Result<StorageReadResult> Read(string key, int maxBytes)
        {
            var mapped = MapKey(key); if (!mapped.IsSuccess) return Result<StorageReadResult>.Failure(mapped.Error);
            if (maxBytes < 0 || maxBytes > MaxFileBytes) return Result<StorageReadResult>.Failure(Error("limit", ErrorKind.InvalidArgument));
            var error = Enter(); if (error.HasValue) return Result<StorageReadResult>.Failure(error.Value);
            bool retained = false;
            Result<StorageReadResult> ReadOwned()
            {
                try
                {
                    int count = transport.BeginRead(mapped.Value, maxBytes);
                    if (count == (int)WeChatFileStatus.Missing) return Result<StorageReadResult>.Success(new StorageReadResult(false));
                    if (count < 0) return Result<StorageReadResult>.Failure(Classify(count));
                    retained = true;
                    if (count > maxBytes) return Result<StorageReadResult>.Failure(Error("length", ErrorKind.Integrity));
                    var bytes = new byte[count]; int copied = transport.FinishRead(bytes);
                    if (copied != 0) return Result<StorageReadResult>.Failure(Classify(copied));
                    return Result<StorageReadResult>.Success(new StorageReadResult(true, bytes));
                }
                catch (Exception) { return Result<StorageReadResult>.Failure(Error("read_exception", ErrorKind.Provider)); }
            }
            try
            {
                var result = ReadOwned();
                try { if (retained) transport.ReleaseRead(); }
                catch (Exception) { result = Result<StorageReadResult>.Failure(Error("release", ErrorKind.CleanupFailed)); }
                return result;
            }
            finally { busy = false; }
        }

        public Result Write(string key, byte[] bytes)
        {
            var mapped = MapKey(key); if (!mapped.IsSuccess) return Result.Failure(mapped.Error);
            if (bytes == null || bytes.Length > MaxFileBytes) return Result.Failure(Error("payload", ErrorKind.InvalidArgument));
            var error = Enter(); if (error.HasValue) return Result.Failure(error.Value);
            try { int code = transport.Write(mapped.Value, (byte[])bytes.Clone()); return code == 0 ? Result.Success() : Result.Failure(Classify(code)); }
            catch (Exception) { return Result.Failure(Error("write_exception", ErrorKind.Provider)); }
            finally { busy = false; }
        }
    }
}
