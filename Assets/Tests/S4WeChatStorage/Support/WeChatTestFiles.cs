using System;
using System.Collections.Generic;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat.Storage;

namespace MiniFramework.WeChatStorage.TestSupport
{
    public enum FileFault { None, ThrowBefore, ThrowAfter, Torn, Corrupt, Drop, VerifyRead, Quota }
    // Test-only volatile file port; never used to attest native disk durability.
    public sealed class WeChatTestFiles : IWeChatStorageFiles
    {
        public StoragePersistence Persistence => StoragePersistence.Volatile;
        public readonly Dictionary<string, byte[]> Data = new Dictionary<string, byte[]>();
        public readonly List<string> WriteKeys = new List<string>();
        public string FaultKey, DeniedKey, FailNextReadKey;
        public FileFault Next;
        public Action<string, byte[]> OnWrite;
        public bool MutateArgument;
        public int Reads;
        public Result<StorageReadResult> Read(string key, int maxBytes)
        {
            Reads++;
            if (key == DeniedKey || key == FailNextReadKey)
            { FailNextReadKey = null; return Result<StorageReadResult>.Failure(new FrameworkError(ErrorKind.PermissionDenied, "test.read.denied")); }
            if (!Data.TryGetValue(key, out var bytes)) return Result<StorageReadResult>.Success(new StorageReadResult(false));
            if (bytes.Length > maxBytes) return Result<StorageReadResult>.Failure(new FrameworkError(ErrorKind.Integrity, "test.read.oversize"));
            return Result<StorageReadResult>.Success(new StorageReadResult(true, bytes));
        }
        public Result Write(string key, byte[] bytes)
        {
            WriteKeys.Add(key); var callback = OnWrite; OnWrite = null; callback?.Invoke(key, bytes);
            var fault = key == FaultKey ? Next : FileFault.None;
            if (key == FaultKey) Next = FileFault.None;
            if (fault == FileFault.ThrowBefore) throw new Exception("secret-test-payload");
            if (fault == FileFault.Quota) return Result.Failure(new FrameworkError(ErrorKind.Storage, "test.quota"));
            if (fault != FileFault.Drop) Data[key] = (byte[])bytes.Clone();
            if (MutateArgument && bytes.Length > 0) bytes[0] ^= 1;
            if (fault == FileFault.Torn)
            { Data[key] = new byte[] { 1, 2, 3 }; return Result.Failure(new FrameworkError(ErrorKind.Storage, "test.torn")); }
            if (fault == FileFault.Corrupt && Data[key].Length > 0) Data[key][0] ^= 1;
            if (fault == FileFault.VerifyRead) FailNextReadKey = key;
            if (fault == FileFault.ThrowAfter) throw new Exception("secret-test-payload");
            return Result.Success();
        }
    }
}
