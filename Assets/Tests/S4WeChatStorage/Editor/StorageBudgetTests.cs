using System;
using System.Collections.Generic;
using System.Threading;
using MiniFramework.Contracts;
using MiniFramework.Storage;
using MiniFramework.Providers.WeChat.Storage;
using NUnit.Framework;

public sealed class StorageBudgetTests
{
    sealed class Files:IWeChatStorageFiles,IWeChatStorageFileLimits
    {
        public int MaxFileBytes {get;set;}=1024*1024;
        public StoragePersistence Persistence=>StoragePersistence.Volatile;
        public readonly Dictionary<string,byte[]> Data=new Dictionary<string,byte[]>();
        public int Writes;public bool Quota;
        public Result<StorageReadResult> Read(string key,int maxBytes)
        {
            if(!Data.TryGetValue(key,out var b))return Result<StorageReadResult>.Success(new StorageReadResult(false));
            return b.Length>maxBytes?Result<StorageReadResult>.Failure(new FrameworkError(ErrorKind.Integrity,"test.limit")):Result<StorageReadResult>.Success(new StorageReadResult(true,b));
        }
        public Result Write(string key,byte[] bytes)
        {Writes++;if(Quota)return Result.Failure(new FrameworkError(ErrorKind.Storage,"test.quota"));Data[key]=(byte[])bytes.Clone();return Result.Success();}
    }
    static readonly StorageAddress Address=new StorageAddress("budget","save");
    static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);
    static byte[] Encode(int size,long sequence,StorageLimits limits)=>StorageFormat.Encode(new StorageContainer(Address,sequence,new[]{new StorageRecord("progress",1,"bytes",true,new byte[size])}),limits).Value;
    [Test] public void DefaultsStayCompatibleButLargerContainerIsSelectable()
    {Assert.AreEqual(65536,new WeChatStorageOptions().MaxContainerBytes);var o=new WeChatStorageOptions(1024*1024);Assert.AreEqual(1024*1024,o.CreateServiceLimits().MaxContainerBytes);Assert.AreEqual(65536,o.CreateServiceLimits().MaxPayloadBytes);}
    [TestCase(255)][TestCase(16*1024*1024+1)] public void WholeBufferEnvelopeRemainsExplicit(int size)
    {Assert.Throws<ArgumentOutOfRangeException>(()=>new WeChatStorageOptions(size));}
    [Test] public void ServicePayloadBudgetCanBeRaisedIndependently()
    {var o=new WeChatStorageOptions(1024*1024);var l=o.CreateServiceLimits(maxPayloadBytes:256*1024);Assert.AreEqual(256*1024,l.MaxPayloadBytes);Assert.Throws<ArgumentOutOfRangeException>(()=>o.CreateServiceLimits(maxPayloadBytes:2*1024*1024));}
    [Test] public void ProviderRejectsBudgetAboveDeclaredPortBeforeIo()
    {var f=new Files{MaxFileBytes=65536};Assert.Throws<ArgumentException>(()=>new WeChatStorageProvider(f,new WeChatStorageOptions(1024*1024)));Assert.AreEqual(0,f.Writes);}
    [Test] public void LargerSaveCommitsReopensAndRepairsWithoutChangingFormat()
    {
        var f=new Files();var o=new WeChatStorageOptions(1024*1024);var l=o.CreateServiceLimits(maxPayloadBytes:256*1024);var b=Encode(128*1024,1,l);
        Assert.Greater(b.Length,65536);var p=new WeChatStorageProvider(f,o);var s=p.Create(Address);
        var result=s.CommitAsync(new StorageWriteRequest(Address,0,"",1,StorageFormat.Digest(b),b)).GetAwaiter().GetResult();Assert.AreEqual(StorageCommitDisposition.Committed,result.Disposition);
        Assert.IsTrue(s.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);
        var reopen=new WeChatStorageProvider(f,o).Create(Address);CollectionAssert.AreEqual(b,reopen.ReadAsync().GetAwaiter().GetResult().Value.CopyBytes());Assert.IsTrue(reopen.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);
        f.Data[WeChatStorageProvider.KeyFor(Address,1)]=new byte[100*1024];
        var inspection=p.InspectRepairAsync(Address).GetAwaiter().GetResult();Assert.IsTrue(inspection.IsSuccess);
        var repaired=p.RepairAsync(inspection.Value).GetAwaiter().GetResult();Assert.AreEqual(WeChatRepairDisposition.Repaired,repaired.Disposition);Assert.IsTrue(repaired.BackupVerified);
    }
    [Test] public void EnlargedLimitDoesNotSuppressQuotaOrOverwriteGoodBank()
    {
        var f=new Files();var o=new WeChatStorageOptions(1024*1024);var l=o.CreateServiceLimits(maxPayloadBytes:256*1024);var first=Encode(128*1024,1,l);
        f.Data[WeChatStorageProvider.KeyFor(Address,0)]=first;var next=Encode(128*1024,2,l);f.Quota=true;
        var p=new WeChatStorageProvider(f,o);var s=p.Create(Address);Assert.AreEqual(StorageCommitDisposition.Indeterminate,s.CommitAsync(new StorageWriteRequest(Address,1,StorageFormat.Digest(first),2,StorageFormat.Digest(next),next)).GetAwaiter().GetResult().Disposition);
        CollectionAssert.AreEqual(first,f.Data[WeChatStorageProvider.KeyFor(Address,0)]);Assert.IsTrue(s.CleanupAsync(Cleanup).GetAwaiter().GetResult().IsSuccess);
    }
}
