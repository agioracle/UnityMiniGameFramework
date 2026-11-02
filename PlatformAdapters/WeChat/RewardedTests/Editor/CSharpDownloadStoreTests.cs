using System;
using System.Collections.Generic;
using System.Linq;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat.WxStorage;
using NUnit.Framework;

public sealed class CSharpDownloadStoreTests
{
    sealed class Files:WxHttpDownloadStore.IFiles
    {
        public readonly Dictionary<string,byte[]> Data=new Dictionary<string,byte[]>();
        public bool FailDelete,FailAppend,ThrowAfterMove,DropMove;public int Appends,MaxAppend,Deletes;
        public void EnsureRoot(string root) { }
        public string[] List(string root)=>Data.Keys.Select(k=>k.Substring(root.Length+1)).ToArray();
        public long? Size(string path)=>Data.TryGetValue(path,out var b)?b.LongLength:(long?)null;
        public void Create(string path){if(Data.ContainsKey(path))throw new Exception();Data.Add(path,Array.Empty<byte>());}
        public void Append(string path,byte[] b){Appends++;MaxAppend=Math.Max(MaxAppend,b.Length);if(FailAppend)throw new Exception();Data[path]=Data[path].Concat(b).ToArray();}
        public void Move(string source,string target){if(DropMove)return;if(Data.ContainsKey(target))throw new Exception();Data.Add(target,Data[source]);Data.Remove(source);if(ThrowAfterMove)throw new Exception();}
        public void Delete(string path){Deletes++;if(FailDelete)throw new Exception();Data.Remove(path);}
    }
    static HttpDownloadRequest Spec(long n,string target="model")=>new HttpDownloadRequest(new HttpRequestSpec(HttpVerb.Get,"https://example.test/file"),target,n,new string('0',64));
    [Test] public void BoundedChunksPublishAbove64KiBAndCleanupKeepsTarget()
    {
        var f=new Files();var store=new WxHttpDownloadStore(f,"/root",1024*1024,512*1024);var file=store.Create(Spec(128*1024)).Value;
        Assert.IsEmpty(f.Data);for(int i=0;i<8;i++)Assert.IsTrue(file.Append(new byte[16384],16384).IsSuccess);
        Assert.AreEqual(16384,f.MaxAppend);Assert.IsTrue(file.Seal().IsSuccess);Assert.IsTrue(file.Publish().IsSuccess);Assert.IsTrue(file.Cleanup().IsSuccess);Assert.AreEqual(128*1024,f.Data["/root/model.download"].Length);Assert.AreEqual(1,f.Data.Count);
    }
    [Test] public void ActiveReservationsAndOldStagesConsumeQuota()
    {var f=new Files();var store=new WxHttpDownloadStore(f,"/root",1024,1024);var first=store.Create(Spec(800)).Value;Assert.IsFalse(store.Create(Spec(800,"second")).IsSuccess);Assert.IsTrue(first.Cleanup().IsSuccess);f.Data["/root/old.part"]=new byte[800];Assert.IsFalse(store.Create(Spec(800)).IsSuccess);Assert.AreEqual(800,f.Data["/root/old.part"].Length);}
    [Test] public void DefaultChunkBudgetCannotBeBypassedByCaller()
    {var f=new Files();var file=new WxHttpDownloadStore(f,"/root",1024*1024,512*1024).Create(Spec(128*1024)).Value;Assert.IsFalse(file.Append(new byte[65536],65536).IsSuccess);Assert.AreEqual(0,f.Appends);Assert.IsTrue(file.Cleanup().IsSuccess);}
    [Test] public void FailedDeleteRetainsStageForExplicitRetry()
    {var f=new Files();var file=new WxHttpDownloadStore(f,"/root",1024,1024).Create(Spec(2)).Value;Assert.IsTrue(file.Append(new byte[1],1).IsSuccess);f.FailDelete=true;Assert.AreEqual(ErrorKind.CleanupFailed,file.Cleanup().Error.Kind);Assert.AreEqual(1,f.Data.Count);f.FailDelete=false;Assert.IsTrue(file.Cleanup().IsSuccess);Assert.IsEmpty(f.Data);}
    [Test] public void ExistingTargetAndLatePublishConflictNeverOverwrite()
    {var f=new Files();var store=new WxHttpDownloadStore(f,"/root",1024,1024);var file=store.Create(Spec(1)).Value;Assert.IsTrue(file.Append(new byte[]{1},1).IsSuccess);Assert.IsTrue(file.Seal().IsSuccess);f.Data["/root/model.download"]=new byte[]{9};Assert.IsFalse(file.Publish().IsSuccess);Assert.IsTrue(file.Cleanup().IsSuccess);CollectionAssert.AreEqual(new byte[]{9},f.Data["/root/model.download"]);Assert.IsFalse(store.Create(Spec(1)).IsSuccess);}
    [Test] public void AppendFailureDiscardsOnlyOwnedStage()
    {var f=new Files{FailAppend=true};f.Data["/root/other.download"]=new byte[]{9};var file=new WxHttpDownloadStore(f,"/root",1024,1024).Create(Spec(1)).Value;Assert.IsFalse(file.Append(new byte[1],1).IsSuccess);Assert.IsTrue(file.Cleanup().IsSuccess);Assert.AreEqual(1,f.Data.Count);}
    [Test] public void UnknownMoveResultDoesNotDeletePossiblyPublishedFile()
    {var f=new Files{ThrowAfterMove=true};var file=new WxHttpDownloadStore(f,"/root",1024,1024).Create(Spec(1)).Value;Assert.IsTrue(file.Append(new byte[]{7},1).IsSuccess);Assert.IsTrue(file.Seal().IsSuccess);Assert.AreEqual("wechat.download.publish_unknown",file.Publish().Error.Code);Assert.IsTrue(file.Cleanup().IsSuccess);CollectionAssert.AreEqual(new byte[]{7},f.Data["/root/model.download"]);}
    [Test] public void PerFileBudgetIsSeparateFromTotalDiskBudget()
    {var f=new Files();var store=new WxHttpDownloadStore(f,"/root",1024*1024,1024);Assert.IsFalse(store.Create(Spec(2048)).IsSuccess);Assert.IsEmpty(f.Data);}
    [Test] public void VoidRenameWithNoEffectCannotReportPublication()
    {var f=new Files{DropMove=true};var file=new WxHttpDownloadStore(f,"/root",1024,1024).Create(Spec(1)).Value;Assert.IsTrue(file.Append(new byte[]{7},1).IsSuccess);Assert.IsTrue(file.Seal().IsSuccess);Assert.AreEqual("wechat.download.publish_unknown",file.Publish().Error.Code);Assert.IsTrue(file.Cleanup().IsSuccess);Assert.IsEmpty(f.Data);}
    [Test] public void RecursiveSdkMetadataSelectsExactFileInsteadOfRootDirectory()
    {
        // Captured official SDK v3 shape: root plus slash-prefixed relative file rows.
        var rows=new[]{new WeChatWASM.WXStat{path="/",stats=new WeChatWASM.Stats{mode=16877,size=96}},new WeChatWASM.WXStat{path="/metadata-probe",stats=new WeChatWASM.Stats{mode=33188,size=128}}};
        Assert.AreEqual(128,WxHttpDownloadStore.ReadRecursiveFileSize(rows,"metadata-probe"));
    }
    [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)]
    public void RecursiveSdkMetadataRejectsMissingAmbiguousOrInvalidFile(int scenario)
    {
        var good=new WeChatWASM.WXStat{path="/file",stats=new WeChatWASM.Stats{mode=33188,size=128}};
        WeChatWASM.WXStat[] rows;
        switch(scenario)
        {
            case 0:rows=null;break;
            case 1:rows=new[]{new WeChatWASM.WXStat{path="/",stats=new WeChatWASM.Stats{mode=16877,size=96}}};break;
            case 2:rows=new[]{good,good};break;
            case 3:rows=new[]{new WeChatWASM.WXStat{path="/file",stats=null}};break;
            case 4:rows=new[]{new WeChatWASM.WXStat{path="/file",stats=new WeChatWASM.Stats{mode=16877,size=128}}};break;
            default:rows=new[]{new WeChatWASM.WXStat{path="/file",stats=new WeChatWASM.Stats{mode=33188,size=-1}}};break;
        }
        Assert.Throws<InvalidOperationException>(()=>WxHttpDownloadStore.ReadRecursiveFileSize(rows,"file"));
    }
    [TestCase("file")][TestCase("/FILE")][TestCase("/nested/file")][TestCase("/../file")]
    public void RecursiveSdkMetadataDoesNotNormalizeOrFollowOtherPaths(string sdkPath)
    {var rows=new[]{new WeChatWASM.WXStat{path=sdkPath,stats=new WeChatWASM.Stats{mode=33188,size=128}}};Assert.Throws<InvalidOperationException>(()=>WxHttpDownloadStore.ReadRecursiveFileSize(rows,"file"));}
    [Test] public void RecursiveSdkMetadataAcceptsZeroLengthRegularFile()
    {Assert.AreEqual(0,WxHttpDownloadStore.ReadRecursiveFileSize(new[]{new WeChatWASM.WXStat{path="/file",stats=new WeChatWASM.Stats{mode=33188,size=0}}},"file"));}
}
