using System;
using System.Text;
using System.Threading;
using MiniFramework.Contracts;
using MiniFramework.Storage;
using MiniFramework.Providers.WeChat.Storage;
using NUnit.Framework;

public sealed class WeChatNativeFilePortTests
{
    sealed class Transport : IWeChatFileTransport
    {
        internal int Begin = 2, Finish, Written, Reads, Finishes, Releases, Writes;
        internal bool ThrowBegin, ThrowFinish, ThrowWrite, ThrowRelease;
        internal string Key; internal byte[] Retained;
        internal Action Callback;
        public int BeginRead(string key, int maxBytes) { Reads++; Key = key; Callback?.Invoke(); if (ThrowBegin) throw new Exception("private-path-value"); return Begin; }
        public int FinishRead(byte[] bytes) { Finishes++; if (ThrowFinish) throw new Exception("private-path-value"); for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)i; return Finish; }
        public void ReleaseRead() { Releases++; if (ThrowRelease) throw new Exception("private-path-value"); }
        public int Write(string key, byte[] bytes) { Writes++; Key = key; Retained = bytes; if (bytes.Length > 0) bytes[0] = 99; if (ThrowWrite) throw new Exception("private-path-value"); return Written; }
    }
    Transport transport; WeChatNativeFilePort port;
    [SetUp] public void Setup() { transport = new Transport(); port = new WeChatNativeFilePort(transport, StoragePersistence.Volatile); }
    [Test] public void MissingIsOnlySuccessfulAbsentResult() { transport.Begin = -1; var r = port.Read("slot.0", 16); Assert.IsTrue(r.IsSuccess); Assert.IsFalse(r.Value.Exists); Assert.AreEqual(0, transport.Finishes + transport.Releases); }
    [Test] public void ZeroLengthIsExistingNotMissing() { transport.Begin = 0; var r = port.Read("slot.0", 0); Assert.IsTrue(r.IsSuccess); Assert.IsTrue(r.Value.Exists); Assert.AreEqual(0, r.Value.CopyBytes().Length); Assert.AreEqual(1, transport.Releases); }
    [TestCase(-2, ErrorKind.PermissionDenied)] [TestCase(-3, ErrorKind.Storage)] [TestCase(-4, ErrorKind.Integrity)]
    [TestCase(-5, ErrorKind.InvalidArgument)] [TestCase(-6, ErrorKind.NotSupported)] [TestCase(-7, ErrorKind.Provider)]
    [TestCase(-8, ErrorKind.Integrity)] [TestCase(-9, ErrorKind.InvalidState)] [TestCase(-123, ErrorKind.Integrity)]
    public void NegativeStatusNeverBecomesMissing(int status, ErrorKind kind) { transport.Begin = status; var r = port.Read("slot.0", 16); Assert.IsFalse(r.IsSuccess); Assert.AreEqual(kind, r.Error.Kind); Assert.AreEqual(0, transport.Finishes + transport.Releases); }
    [TestCase(-1)] [TestCase(65537)] public void InvalidLimitDoesNotEnterTransport(int limit) { Assert.IsFalse(port.Read("slot.0", limit).IsSuccess); Assert.AreEqual(0, transport.Reads); }
    [Test] public void ReturnedLengthCannotExceedBoundAndOwnedBufferIsReleased() { transport.Begin = 17; Assert.IsFalse(port.Read("slot.0", 16).IsSuccess); Assert.AreEqual(0, transport.Finishes); Assert.AreEqual(1, transport.Releases); }
    [Test] public void SuccessfulReadCopiesExactlyAndReleasesHostBuffer() { var r = port.Read("slot.0", 16); CollectionAssert.AreEqual(new byte[] { 0, 1 }, r.Value.CopyBytes()); Assert.AreEqual(1, transport.Releases); }
    [Test] public void BeginExceptionIsSanitizedAndDoesNotReleaseUnownedBuffer() { transport.ThrowBegin = true; Assert.AreEqual("wechat.files.read_exception", port.Read("slot.0", 16).Error.Code); Assert.AreEqual(0, transport.Releases); }
    [Test] public void FinishExceptionStillReleasesOwnedBuffer() { transport.ThrowFinish = true; Assert.AreEqual("wechat.files.read_exception", port.Read("slot.0", 16).Error.Code); Assert.AreEqual(1, transport.Releases); }
    [Test] public void MissingDuringCopyIsAFailureNotNewSave() { transport.Finish = -1; Assert.IsFalse(port.Read("slot.0", 16).IsSuccess); Assert.AreEqual(1, transport.Releases); }
    [Test] public void ReleaseFailureIsClassifiedAndDoesNotEscapeResult() { transport.ThrowRelease = true; Assert.AreEqual(ErrorKind.CleanupFailed, port.Read("slot.0", 16).Error.Kind); transport.ThrowRelease = false; Assert.IsTrue(port.Read("slot.0", 16).IsSuccess); }
    [Test] public void WriteReceivesOwnedClone() { var bytes = new byte[] { 7 }; Assert.IsTrue(port.Write("slot.0", bytes).IsSuccess); Assert.AreEqual(7, bytes[0]); }
    [Test] public void WriteMissingStatusIsNotSuccess() { transport.Written = -1; Assert.AreEqual("wechat.files.missing", port.Write("slot.0", new byte[] { 7 }).Error.Code); }
    [Test] public void WriteExceptionIsSanitized() { transport.ThrowWrite = true; Assert.AreEqual("wechat.files.write_exception", port.Write("slot.0", new byte[] { 7 }).Error.Code); }
    [Test] public void NullWriteDoesNotEnterTransport() { Assert.IsFalse(port.Write("slot.0", null).IsSuccess); Assert.AreEqual(0, transport.Writes); }
    [Test] public void OversizeWriteDoesNotEnterTransport() { Assert.IsFalse(port.Write("slot.0", new byte[65537]).IsSuccess); Assert.AreEqual(0, transport.Writes); }
    [TestCase(null)] [TestCase("")] [TestCase("../x")] [TestCase("a/b")] [TestCase("a\\b")] [TestCase("中文")]
    public void InvalidLogicalKeysNeverEnterTransport(string key) { Assert.IsFalse(port.Read(key, 16).IsSuccess); Assert.IsFalse(port.Write(key, new byte[] { 7 }).IsSuccess); Assert.AreEqual(0, transport.Reads + transport.Writes); }
    [Test] public void TooLongKeyIsRejected() { Assert.IsFalse(port.Read(new string('a', 513), 16).IsSuccess); Assert.AreEqual(0, transport.Reads); }
    [Test] public void LongDualBankBackupKeyMapsToFixedSafeFilename() { string key = WeChatStorageProvider.KeyFor(new StorageAddress(new string('a', 64), new string('b', 64)), 1) + ".bad-" + new string('c', 64); port.Read(key, 16); Assert.AreEqual(64, transport.Key.Length); Assert.AreEqual(StorageFormat.Digest(Encoding.ASCII.GetBytes(key)), transport.Key); }
    [Test] public void ReentrantReadDoesNotReleaseOuterBuffer() { transport.Callback = () => { transport.Callback = null; Assert.AreEqual("wechat.files.busy", port.Read("slot.0", 16).Error.Code); }; Assert.IsTrue(port.Read("slot.0", 16).IsSuccess); Assert.AreEqual(1, transport.Releases); }
    [Test] public void WorkerCallsNeverEnterTransport() { Result<StorageReadResult> r = default; Result w = default; var t = new Thread(() => { r = port.Read("slot.0", 16); w = port.Write("slot.0", new byte[] { 7 }); }); t.Start(); t.Join(); Assert.AreEqual("wechat.files.thread", r.Error.Code); Assert.AreEqual("wechat.files.thread", w.Error.Code); Assert.AreEqual(0, transport.Reads + transport.Writes); }
    [Test] public void PersistenceIsExplicitNeverAutomaticallyUpgraded() { Assert.AreEqual(StoragePersistence.Volatile, port.Persistence); Assert.AreEqual(StoragePersistence.RestartRecoverable, new WeChatNativeFilePort(transport, StoragePersistence.RestartRecoverable).Persistence); }
}
