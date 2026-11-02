using System;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat.Storage;
using MiniFramework.Providers.WeChat.WxSdk;
using WeChatWASM;

namespace MiniFramework.Providers.WeChat.WxStorage
{
    public static class WxStorageFiles
    {
        // Cold C# SDK factory. No directory creation or writes.
        public static Result<IWeChatStorageFiles> CreateNative(string rootId = "saves-v1", WeChatStorageOptions options=null)
        {
            if (!PlayerLoopHelper.IsMainThread) return Fail(ErrorKind.InvalidState, "wechat.files.main_thread");
            if (!StorageIdentifier.IsValid(rootId) || rootId.Contains(".")) return Fail(ErrorKind.InvalidArgument, "wechat.files.root");
#if UNITY_WEBGL && !UNITY_EDITOR
            var host = WxSdkApi.CreateNativeApi();
            if (!host.IsSuccess) return Result<IWeChatStorageFiles>.Failure(host.Error);
            try
            {
                var path = WX.env.USER_DATA_PATH;
                if (!string.IsNullOrEmpty(path) && path.Length <= 1024 && path.IndexOf('\0') < 0)
                    return Result<IWeChatStorageFiles>.Success(new CSharpFiles(new SdkFiles(WX.GetFileSystemManager()), path.TrimEnd('/') + "/miniframework-" + rootId,options));
            }
            catch (Exception) { }
#endif
            return Fail(ErrorKind.NotSupported, "wechat.files.native_host_required");
        }
        static Result<IWeChatStorageFiles> Fail(ErrorKind kind, string code) => Result<IWeChatStorageFiles>.Failure(new FrameworkError(kind, code));
        internal static int Classify(string text)
        {
            text = text ?? "";
            text = Regex.Replace(text, @"^((?:accessSync|readFileSync|writeFileSync|mkdirSync):\s*fail\s+)fail\s+", "$1", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(text, @"(?:^|:\s*fail\s+)(?:EACCES\b|EPERM\b|permission denied\b|operation not permitted\b)", RegexOptions.IgnoreCase)) return -2;
            if (Regex.IsMatch(text, @"(?:^|:\s*fail\s+)(?:ENOSPC\b|EDQUOT\b|no space left on device\b|quota exceeded\b|the maximum size of the file storage limit is exceeded\b)", RegexOptions.IgnoreCase)) return -3;
            if (Regex.IsMatch(text, @"(?:^|:\s*fail\s+)(?:ENOENT\b|no such file or directory\b)", RegexOptions.IgnoreCase)) return -1;
            return -7;
        }
        internal interface IFiles
        {
            string Access(string path);
            string Mkdir(string path);
            string Open(string path);
            Stats Stat(string fd);
            ReadResult Read(string fd, byte[] destination);
            void Close(string fd);
            string Write(string path, byte[] data);
        }
        internal sealed class CSharpFiles : IWeChatStorageFiles, IWeChatStorageCleanup, IWeChatStorageFileLimits
        {
            readonly IFiles sdk;
            readonly string root;
            string retainedFd;
            bool busy, uncertainOpen;
            public StoragePersistence Persistence => StoragePersistence.RestartRecoverable;
            public bool CleanupPending => retainedFd != null || uncertainOpen;
            public int MaxFileBytes {get;}
            internal CSharpFiles(IFiles sdk, string root, WeChatStorageOptions options=null) { this.sdk = sdk ?? throw new ArgumentNullException(nameof(sdk)); this.root = root; MaxFileBytes=(options??new WeChatStorageOptions()).MaxContainerBytes; }
            static FrameworkError Error(string code, ErrorKind kind = ErrorKind.Provider) => new FrameworkError(kind, "wechat.files." + code);
            FrameworkError? Enter()
            {
                if (!PlayerLoopHelper.IsMainThread) return Error("thread", ErrorKind.InvalidState);
                if (busy) return Error("busy", ErrorKind.InvalidState);
                if (CleanupPending) return Error("cleanup_required", ErrorKind.CleanupFailed);
                busy = true; return null;
            }
            public Result<StorageReadResult> Read(string key, int maxBytes)
            {
                var mapped = WeChatNativeFilePort.MapKey(key);
                if (!mapped.IsSuccess) return Result<StorageReadResult>.Failure(mapped.Error);
                if (maxBytes < 0 || maxBytes > MaxFileBytes) return Result<StorageReadResult>.Failure(Error("limit", ErrorKind.InvalidArgument));
                var entry = Enter(); if (entry.HasValue) return Result<StorageReadResult>.Failure(entry.Value);
                Result<StorageReadResult> result;
                try { result = ReadOwned(root + "/" + mapped.Value, maxBytes); }
                catch (Exception) { result = Result<StorageReadResult>.Failure(Error("read_exception")); }
                // Own the FD even when Open succeeded but Fstat/Read failed. No read or write replay.
                try { CloseRetained(); }
                catch (Exception) { result = Result<StorageReadResult>.Failure(Error("release", ErrorKind.CleanupFailed)); }
                busy = false; return result;
            }
            Result<StorageReadResult> ReadOwned(string path, int maxBytes)
            {
                string access = sdk.Access(path);
                if (access != "access:ok")
                {
                    int status = Classify(access);
                    return status == -1 ? Result<StorageReadResult>.Success(new StorageReadResult(false)) : Result<StorageReadResult>.Failure(WeChatNativeFilePort.Classify(status));
                }
                try { retainedFd = sdk.Open(path); }
                catch (Exception) { uncertainOpen = true; throw; }
                if (string.IsNullOrEmpty(retainedFd)) { retainedFd = null; uncertainOpen = true; return Result<StorageReadResult>.Failure(Error("fd", ErrorKind.Integrity)); }
                var before = sdk.Stat(retainedFd);
                if (before == null || !before.isFile() || before.size < 0) return Result<StorageReadResult>.Failure(Error("stat", ErrorKind.Integrity));
                if (before.size > maxBytes) return Result<StorageReadResult>.Failure(WeChatNativeFilePort.Classify(-4));
                var bytes = new byte[before.size]; // Allocate only after the SDK C# Fstat bound check.
                if (bytes.Length != 0)
                {
                    var read = sdk.Read(retainedFd, bytes);
                    if (read == null || read.bytesRead != bytes.Length || read.arrayBuffer == null || read.arrayBuffer.Length != bytes.Length)
                        return Result<StorageReadResult>.Failure(Error("length", ErrorKind.Integrity));
                    bytes = read.arrayBuffer;
                }
                var after = sdk.Stat(retainedFd);
                if (after == null || !after.isFile() || after.size != before.size) return Result<StorageReadResult>.Failure(Error("changed", ErrorKind.Integrity));
                return Result<StorageReadResult>.Success(new StorageReadResult(true, bytes));
            }
            public Result Write(string key, byte[] data)
            {
                var mapped = WeChatNativeFilePort.MapKey(key); if (!mapped.IsSuccess) return Result.Failure(mapped.Error);
                if (data == null || data.Length > MaxFileBytes) return Result.Failure(Error("payload", ErrorKind.InvalidArgument));
                var entry = Enter(); if (entry.HasValue) return Result.Failure(entry.Value);
                try
                {
                    var access = sdk.Access(root);
                    if (access != "access:ok")
                    {
                        var code = Classify(access); if (code != -1) return Result.Failure(WeChatNativeFilePort.Classify(code));
                        var mkdir = sdk.Mkdir(root); if (mkdir != "mkdir:ok") return Result.Failure(WeChatNativeFilePort.Classify(Classify(mkdir)));
                    }
                    var written = sdk.Write(root + "/" + mapped.Value, (byte[])data.Clone());
                    return written == "ok" ? Result.Success() : Result.Failure(WeChatNativeFilePort.Classify(Classify(written)));
                }
                catch (Exception) { return Result.Failure(Error("write_exception")); }
                finally { busy = false; }
            }
            void CloseRetained() { if (retainedFd != null) { sdk.Close(retainedFd); retainedFd = null; } }
            public Result RetryCleanup()
            {
                if (!PlayerLoopHelper.IsMainThread || busy) return Result.Failure(Error("cleanup_busy", ErrorKind.CleanupFailed));
                // Open may have allocated an unreturned FD. No SDK API can recover its identity.
                if (uncertainOpen) return Result.Failure(Error("open_uncertain", ErrorKind.CleanupFailed));
                try { CloseRetained(); return Result.Success(); }
                catch (Exception) { return Result.Failure(Error("release", ErrorKind.CleanupFailed)); }
            }
        }
        sealed class SdkFiles : IFiles
        {
            readonly WXFileSystemManager fs;
            internal SdkFiles(WXFileSystemManager fs) { this.fs = fs; }
            public string Access(string path) => fs.AccessSync(path);
            public string Mkdir(string path) => fs.MkdirSync(path, false);
            public string Open(string path) => fs.OpenSync(new OpenSyncOption { filePath = path, flag = "r" });
            public Stats Stat(string fd) => fs.FstatSync(new FstatSyncOption { fd = fd });
            public ReadResult Read(string fd, byte[] data) => fs.ReadSync(new ReadSyncOption { fd = fd, arrayBuffer = data, arrayBufferLength = data.Length, offset = 0, position = 0, length = data.Length });
            public void Close(string fd) => fs.CloseSync(new CloseSyncOption { fd = fd });
            public string Write(string path, byte[] bytes) => fs.WriteFileSync(path, bytes, null);
        }
    }
}
