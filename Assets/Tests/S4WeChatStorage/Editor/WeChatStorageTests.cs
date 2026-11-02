using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Storage;
using MiniFramework.Providers.WeChat.Storage;
using NUnit.Framework;
public sealed class WeChatStorageTests
{
    static readonly StorageAddress Address = new StorageAddress("local", "main");
    static readonly StorageLimits Limits = new StorageLimits();
    static CleanupContext Cleanup => new CleanupContext(default, Timeout.InfiniteTimeSpan);
    public enum Fault { None, ThrowBefore, ThrowAfter, Torn, Corrupt, Drop, VerifyRead, Quota }
    sealed class Files : IWeChatStorageFiles
    {
        public StoragePersistence Persistence { get; set; } = StoragePersistence.Volatile;
        public readonly Dictionary<string, byte[]> Data = new Dictionary<string, byte[]>();
        public int Reads, Writes; public Fault Next; public bool ReadDenied; public Action OnRead; bool failRead;
        public Result<StorageReadResult> Read(string key, int maxBytes)
        {
            Reads++; var cb = OnRead; OnRead = null; cb?.Invoke();
            if (failRead || ReadDenied) { failRead = false; return Result<StorageReadResult>.Failure(new FrameworkError(ErrorKind.PermissionDenied,"test.permission")); }
            if (!Data.TryGetValue(key, out var bytes)) return Result<StorageReadResult>.Success(new StorageReadResult(false));
            if (bytes.Length > maxBytes) return Result<StorageReadResult>.Failure(new FrameworkError(ErrorKind.Integrity,"test.size"));
            return Result<StorageReadResult>.Success(new StorageReadResult(true,bytes));
        }
        public Result Write(string key, byte[] bytes)
        {
            Writes++; var fault = Next; Next = Fault.None;
            if (fault == Fault.ThrowBefore) throw new Exception("secret-test-value");
            if (fault == Fault.Quota) return Result.Failure(new FrameworkError(ErrorKind.Storage,"test.quota"));
            if (fault != Fault.Drop) Data[key] = (byte[])bytes.Clone();
            if (fault == Fault.Torn) { Data[key] = bytes.Take(9).ToArray(); return Result.Failure(new FrameworkError(ErrorKind.Storage,"test.torn")); }
            if (fault == Fault.Corrupt) Data[key][0] ^= 1;
            if (fault == Fault.VerifyRead) failRead = true;
            if (fault == Fault.ThrowAfter) throw new Exception("secret-test-value");
            return Result.Success();
        }
    }
    Files files; WeChatStorageProvider provider;
    [SetUp] public void Setup() { files=new Files();provider=new WeChatStorageProvider(files); }
    static T Now<T>(UniTask<T> task) { Assert.IsTrue(task.Status.IsCompleted()); return task.GetAwaiter().GetResult(); }
    static byte[] Bytes(long sequence=1, byte value=7, StorageAddress? address=null, int size=1) => StorageFormat.Encode(new StorageContainer(address??Address,sequence,new[]{new StorageRecord("progress",1,"bytes-v1",true,Enumerable.Repeat(value,size).ToArray())}),Limits).Value;
    static StorageWriteRequest Request(long expected=0, byte[] current=null, byte[] next=null, StorageAddress? address=null)
    { next=next??Bytes(expected+1);return new StorageWriteRequest(address??Address,expected,current==null?"":StorageFormat.Digest(current),expected+1,StorageFormat.Digest(next),next); }
    IStorageSession Open()=>provider.Create(Address);
    StorageCommitReceipt Write(StorageWriteRequest request=null)=>Now(Open().CommitAsync(request??Request()));
    byte[] Read()=>Now(Open().ReadAsync()).Value.CopyBytes();
    void Seed(int bank, byte[] bytes)=>files.Data[WeChatStorageProvider.KeyFor(Address,bank)]=(byte[])bytes.Clone();
    [Test] public void ColdCreationHasNoIo() { Open();Assert.AreEqual(0,files.Reads+files.Writes); }
    [Test] public void MissingIsDistinctFromEmptyCorruptFile() { Assert.IsFalse(Now(Open().ReadAsync()).Value.Exists);Seed(0,Array.Empty<byte>());Assert.AreEqual(ErrorKind.Integrity,Now(Open().ReadAsync()).Error.Kind); }
    [Test] public void FreshCommitReadsBackAndReportsVolatile() { var r=Write();Assert.AreEqual(StorageCommitDisposition.Committed,r.Disposition);Assert.AreEqual(StoragePersistence.Volatile,r.Persistence);CollectionAssert.AreEqual(Bytes(),Read()); }
    [Test] public void RestartReconstructsProviderAndSession() { Write();var p=new WeChatStorageProvider(files);CollectionAssert.AreEqual(Bytes(),Now(p.Create(Address).ReadAsync()).Value.CopyBytes()); }
    [Test] public void SecondCommitLeavesPreviousBankUnchanged() { var first=Bytes();Write();Write(Request(1,first));CollectionAssert.AreEqual(first,files.Data[WeChatStorageProvider.KeyFor(Address,0)]);CollectionAssert.AreEqual(Bytes(2),Read()); }
    [Test] public void ThirdCommitAlternatesOnlyInactiveBank() { Write();Write(Request(1,Bytes()));Write(Request(2,Bytes(2)));CollectionAssert.AreEqual(Bytes(2),files.Data[WeChatStorageProvider.KeyFor(Address,1)]);CollectionAssert.AreEqual(Bytes(3),Read()); }
    [Test] public void StaleOtherProviderCannotOverwrite() { Write();var other=new WeChatStorageProvider(files);Assert.AreEqual("storage.backend.conflict",Now(other.Create(Address).CommitAsync(Request())).Error.Value.Code);Assert.AreEqual(1,files.Writes); }
    [Test] public void SameSequenceDifferentDigestIsConflict() { Write();Assert.AreEqual("storage.backend.conflict",Write(Request(1,Bytes(value:8))).Error.Value.Code); }
    [Test] public void SameSequenceSplitBrainFailsClosed() { Seed(0,Bytes());Seed(1,Bytes(value:8));Assert.AreEqual("wechat.storage.split_brain",Now(Open().ReadAsync()).Error.Code);Assert.AreEqual(StorageCommitDisposition.Rejected,Write().Disposition);Assert.AreEqual(0,files.Writes); }
    [Test] public void SameSequenceIdenticalCopiesAreReadable() { Seed(0,Bytes());Seed(1,Bytes());CollectionAssert.AreEqual(Bytes(),Read());Assert.AreEqual(StorageCommitDisposition.Committed,Write(Request(1,Bytes())).Disposition); }
    [TestCase(0)] [TestCase(1)] public void CorruptOneBankFallsBackButPreservesForRepair(int bank) { Seed(bank,new byte[]{1,2,3});Seed(1-bank,Bytes());CollectionAssert.AreEqual(Bytes(),Read());Assert.AreEqual("wechat.storage.repair_required",Write(Request(1,Bytes())).Error.Value.Code);CollectionAssert.AreEqual(new byte[]{1,2,3},files.Data[WeChatStorageProvider.KeyFor(Address,bank)]); }
    [Test] public void BothCorruptNeverBecomeNewSave() { Seed(0,new byte[]{1});Seed(1,new byte[]{2});Assert.IsFalse(Now(Open().ReadAsync()).IsSuccess);Assert.AreEqual(StorageCommitDisposition.Rejected,Write().Disposition);Assert.AreEqual(0,files.Writes); }
    [Test] public void ForeignAddressFailsEvenWithOtherValidBank() { Seed(0,Bytes());Seed(1,Bytes(2,address:new StorageAddress("other","main")));Assert.AreEqual("wechat.storage.address",Now(Open().ReadAsync()).Error.Code); }
    [Test] public void FutureFormatCannotBeOverwrittenByOlderCode() { Seed(0,Bytes());var b=Bytes(2);b[8]=2;using(var sha=System.Security.Cryptography.SHA256.Create()){var h=sha.ComputeHash(b.Take(b.Length-32).ToArray());Buffer.BlockCopy(h,0,b,b.Length-32,32);}Seed(1,b);Assert.AreEqual("storage.format.unsupported",Now(Open().ReadAsync()).Error.Code);Assert.AreEqual(StorageCommitDisposition.Rejected,Write(Request(1,Bytes())).Disposition); }
    [Test] public void PermissionIsNotMissing() { files.ReadDenied=true;Assert.AreEqual(ErrorKind.PermissionDenied,Now(Open().ReadAsync()).Error.Kind);Assert.AreEqual(StorageCommitDisposition.Rejected,Write().Disposition);Assert.AreEqual(0,files.Writes); }
    [Test] public void OversizeReadFailsBeforeReturnedAllocation() { Seed(0,new byte[65537]);Assert.AreEqual("test.size",Now(Open().ReadAsync()).Error.Code); }
    [Test] public void OversizeRequestDoesNotWrite() { Assert.AreEqual(StorageCommitDisposition.Rejected,Write(Request(next:Bytes(size:65536))).Disposition);Assert.AreEqual(0,files.Writes); }
    [Test] public void DigestMismatchDoesNotWrite() { var b=Bytes();var r=new StorageWriteRequest(Address,0,"",1,"wrong",b);Assert.AreEqual("storage.backend.request",Write(r).Error.Value.Code);Assert.AreEqual(0,files.Writes); }
    [Test] public void NullRequestFailsWithoutMutation() { Assert.AreEqual(StorageCommitDisposition.Rejected,Now(Open().CommitAsync(null)).Disposition);Assert.AreEqual(0,files.Writes); }
    [Test] public void WrongRequestAddressFailsWithoutMutation() { Assert.AreEqual(StorageCommitDisposition.Rejected,Write(Request(address:new StorageAddress("other","main"))).Disposition);Assert.AreEqual(0,files.Writes); }
    [TestCase(Fault.ThrowBefore)] [TestCase(Fault.ThrowAfter)] [TestCase(Fault.Torn)] [TestCase(Fault.Corrupt)] [TestCase(Fault.Drop)] [TestCase(Fault.VerifyRead)] [TestCase(Fault.Quota)]
    public void AttemptedWriteFailureIsUnknownAndSessionMustReconcile(Fault fault) { Write();var old=(byte[])files.Data[WeChatStorageProvider.KeyFor(Address,0)].Clone();var s=Open();files.Next=fault;var r=Now(s.CommitAsync(Request(1,Bytes())));Assert.AreEqual(StorageCommitDisposition.Indeterminate,r.Disposition);CollectionAssert.AreEqual(old,files.Data[WeChatStorageProvider.KeyFor(Address,0)]);Assert.AreEqual("wechat.storage.reconcile",Now(s.ReadAsync()).Error.Code);Assert.AreEqual(StorageCommitDisposition.Rejected,Now(s.CommitAsync(Request(1,Bytes()))).Disposition);Assert.IsTrue(Now(s.CleanupAsync(Cleanup)).IsSuccess); }
    [Test] public void UnknownAfterWriteReopenFindsNewSequence() { Write();files.Next=Fault.ThrowAfter;Assert.AreEqual(StorageCommitDisposition.Indeterminate,Write(Request(1,Bytes())).Disposition);CollectionAssert.AreEqual(Bytes(2),Read()); }
    [Test] public void TornWriteReopenRecoversOldAndRetainsTornFile() { Write();files.Next=Fault.Torn;Write(Request(1,Bytes()));CollectionAssert.AreEqual(Bytes(),Read());Assert.AreEqual(9,files.Data[WeChatStorageProvider.KeyFor(Address,1)].Length); }
    [Test] public void ReadCopiesDoNotMutateStoredData() { Write();var b=Read();b[0]=0;CollectionAssert.AreEqual(Bytes(),Read()); }
    [Test] public void ClosedSessionRejectsIoAndCleanupIsIdempotent() { var s=Open();Assert.IsTrue(Now(s.CleanupAsync(Cleanup)).IsSuccess);Assert.IsTrue(Now(s.CleanupAsync(Cleanup)).IsSuccess);Assert.IsFalse(Now(s.ReadAsync()).IsSuccess);Assert.AreEqual(StorageCommitDisposition.Rejected,Now(s.CommitAsync(Request())).Disposition);Assert.AreEqual(0,files.Reads+files.Writes); }
    [Test] public void ReentrantReadAndCleanupFailWithoutReleasingOuterLease() { var s=Open();Result cleanup=default;Result<StorageReadResult> nested=default;files.OnRead=()=>{nested=Now(s.ReadAsync());cleanup=Now(s.CleanupAsync(Cleanup));};Assert.IsTrue(Now(s.ReadAsync()).IsSuccess);Assert.AreEqual("wechat.storage.busy",nested.Error.Code);Assert.IsFalse(cleanup.IsSuccess);Assert.AreEqual(StorageCommitDisposition.Committed,Now(s.CommitAsync(Request())).Disposition); }
    [Test] public void WorkerCallDoesNotTouchFilePort() { var s=Open();Result<StorageReadResult> result=default;var t=new Thread(()=>result=s.ReadAsync().GetAwaiter().GetResult());t.Start();t.Join();Assert.AreEqual("wechat.storage.thread",result.Error.Code);Assert.AreEqual(0,files.Reads); }
    [Test] public void CapabilitiesDoNotUpgradeVolatilePort() { Assert.AreEqual(StoragePersistence.Volatile,provider.Capabilities.Persistence);Assert.IsTrue(provider.Capabilities.SupportsCompareExchange);Assert.AreEqual(65536,provider.Capabilities.MaxContainerBytes); }
    [TestCase(255)] [TestCase(StorageLimits.MaxSupportedContainerBytes+1)] public void UnsupportedSizeRejected(int max) { Assert.Throws<ArgumentOutOfRangeException>(()=>new WeChatStorageProvider(files,max)); }
    [Test] public void NamespaceAndSlotHaveDistinctSafeKeys() { var key=WeChatStorageProvider.KeyFor(new StorageAddress("a.b","c-d_e"),0);Assert.IsFalse(key.Contains("a.b"));Assert.IsFalse(key.Contains("c-d_e"));Assert.AreNotEqual(key,WeChatStorageProvider.KeyFor(new StorageAddress("a","b.c-d_e"),0));Assert.Throws<ArgumentException>(()=>WeChatStorageProvider.KeyFor(default,0)); }
    [Test] public void UnknownOptionalRecordSurvivesWholeContainerCommit() { var b=StorageFormat.Encode(new StorageContainer(Address,1,new[]{new StorageRecord("progress",1,"bytes-v1",true,new byte[]{7}),new StorageRecord("addon",4,"opaque",false,new byte[]{99,98})}),Limits).Value;Write(Request(next:b));var decoded=StorageFormat.Decode(Read(),Limits).Value;CollectionAssert.AreEqual(new byte[]{99,98},decoded.Records.Single(r=>r.ModuleId=="addon").CopyPayload()); }
    [Test] public void MigrationPreparesCopyThenCommitsOneSequence() { Write();var migrations=new StorageMigrationRegistry();migrations.Register(new StorageMigrationStep("progress",1,"bytes-v1","bytes-v2",b=>Result<byte[]>.Success(b.Concat(new byte[]{9}).ToArray())));var registration=new StorageModuleRegistration("progress",2,"bytes-v2",true,typeof(byte[]),()=>Result<byte[]>.Success(new byte[]{7,9}),b=>Result.Success());var prepared=new StorageRecovery(migrations).Prepare(StorageFormat.Decode(Read(),Limits).Value,new[]{registration},Limits);Assert.IsTrue(prepared.IsSuccess);CollectionAssert.AreEqual(Bytes(),Read());var next=StorageFormat.Encode(new StorageContainer(Address,2,prepared.Value.Records),Limits).Value;Assert.AreEqual(StorageCommitDisposition.Committed,Write(Request(1,Bytes(),next)).Disposition);CollectionAssert.AreEqual(new byte[]{7,9},StorageFormat.Decode(Read(),Limits).Value.Records[0].CopyPayload()); }
}
