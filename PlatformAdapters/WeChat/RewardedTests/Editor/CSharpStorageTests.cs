using System;
using System.Threading;
using MiniFramework.Contracts;
using MiniFramework.Providers.WeChat.Storage;
using MiniFramework.Providers.WeChat.WxStorage;
using NUnit.Framework;
using WeChatWASM;
public sealed class CSharpStorageTests
{
    sealed class Files : WxStorageFiles.IFiles
    {
        public string AccessResult="access:ok",RootResult="access:ok",WriteResult="ok",MkdirResult="mkdir:ok";
        public bool FailOpen,EmptyFd,FailStat,FailRead,FailClose,Changed,NullRead,ShortRead;
        public int Opens,Reads,Closes,Writes,Mkdirs,Stats;
        public int Size=2;
        public byte[] Written;
        public string Path;
        public Action OnRead;
        public string Access(string path) { Path=path;return path.EndsWith("/root")?RootResult:AccessResult; }
        public string Mkdir(string path) {Mkdirs++;return MkdirResult;}
        public string Open(string path){Opens++;if(FailOpen)throw new Exception("private");return EmptyFd?"":"fd-owned";}
        public Stats Stat(string fd){Stats++;if(FailStat)throw new Exception("private");return new Stats{mode=32768,size=Size+(Changed&&Stats>1?1:0)};}
        public ReadResult Read(string fd,byte[] destination){Reads++;OnRead?.Invoke();if(FailRead)throw new Exception("private");return NullRead?null:new ReadResult{bytesRead=ShortRead?destination.Length-1:destination.Length,arrayBuffer=(byte[])destination.Clone()};}
        public void Close(string fd){Closes++;if(FailClose)throw new Exception("private");}
        public string Write(string path,byte[] data){Writes++;Written=data;if(data.Length>0)data[0]=99;return WriteResult;}
    }
    Files sdk;WxStorageFiles.CSharpFiles files;
    [SetUp]public void Setup(){sdk=new Files();files=new WxStorageFiles.CSharpFiles(sdk,"/private-fixture/root");}
    [TestCase("accessSync:fail no such file or directory <PATH>",-1)]
    [TestCase("accessSync:fail fail no such file or directory <PATH>",-1)]
    [TestCase("writeFileSync:fail fail permission denied <PATH>",-2)]
    [TestCase("accessSync:fail EPERM <PATH>",-2)]
    [TestCase("writeFileSync:fail quota exceeded <PATH>",-3)]
    [TestCase("ENOSPC",-3)]
    [TestCase("accessSync:fail errno 1300002",-7)]
    [TestCase("unrelated no such file or directory",-7)]
    [TestCase("unknown",-7)]
    [TestCase(null,-7)]
    public void CSharpSdkErrorTextIsNarrowlyClassified(string text,int expected){Assert.AreEqual(expected,WxStorageFiles.Classify(text));}
    [Test]public void MissingDoesNotOpenFdOrCreateFiles(){sdk.AccessResult="accessSync:fail no such file or directory";var r=files.Read("save.0",8);Assert.IsTrue(r.IsSuccess);Assert.IsFalse(r.Value.Exists);Assert.AreEqual(0,sdk.Opens+sdk.Reads+sdk.Writes+sdk.Mkdirs);}
    [Test]public void PermissionNeverBecomesMissing(){sdk.AccessResult="accessSync:fail permission denied";Assert.AreEqual(ErrorKind.PermissionDenied,files.Read("save.0",8).Error.Kind);Assert.AreEqual(0,sdk.Opens);}
    [Test]public void EmptyIsExistingAndNeverReadsZeroLengthWithSdk(){sdk.Size=0;var r=files.Read("save.0",0);Assert.IsTrue(r.IsSuccess);Assert.IsTrue(r.Value.Exists);Assert.AreEqual(0,r.Value.CopyBytes().Length);Assert.AreEqual(0,sdk.Reads);Assert.AreEqual(1,sdk.Closes);}
    [Test]public void BoundCheckedBeforeReadAllocation(){sdk.Size=65537;Assert.AreEqual(ErrorKind.Integrity,files.Read("save.0",65536).Error.Kind);Assert.AreEqual(0,sdk.Reads);Assert.AreEqual(1,sdk.Closes);}
    [Test]public void BoundedReadAndFdCloseSucceed(){var r=files.Read("save.0",8);Assert.IsTrue(r.IsSuccess);Assert.AreEqual(2,r.Value.CopyBytes().Length);Assert.AreEqual(1,sdk.Reads);Assert.AreEqual(2,sdk.Stats);Assert.AreEqual(1,sdk.Closes);Assert.IsFalse(files.CleanupPending);}
    [Test]public void SizeChangeFailsClosed(){sdk.Changed=true;Assert.AreEqual(ErrorKind.Integrity,files.Read("save.0",8).Error.Kind);Assert.AreEqual(1,sdk.Closes);}
    [Test]public void NullReadFailsClosedAndClosesFd(){sdk.NullRead=true;Assert.AreEqual(ErrorKind.Integrity,files.Read("save.0",8).Error.Kind);Assert.AreEqual(1,sdk.Closes);}
    [Test]public void ShortReadFailsClosedAndClosesFd(){sdk.ShortRead=true;Assert.AreEqual(ErrorKind.Integrity,files.Read("save.0",8).Error.Kind);Assert.AreEqual(1,sdk.Closes);}
    [Test]public void FstatExceptionClosesPartiallyOpenedFd(){sdk.FailStat=true;var r=files.Read("save.0",8);Assert.IsFalse(r.IsSuccess);Assert.AreEqual("",r.Error.Message);Assert.AreEqual(1,sdk.Closes);}
    [Test]public void ReadExceptionClosesFd(){sdk.FailRead=true;Assert.IsFalse(files.Read("save.0",8).IsSuccess);Assert.AreEqual(1,sdk.Closes);}
    [Test]public void CloseFailureRetainsFdUntilExplicitCleanupWithoutReadReplay(){sdk.FailClose=true;Assert.AreEqual(ErrorKind.CleanupFailed,files.Read("save.0",8).Error.Kind);Assert.IsTrue(files.CleanupPending);Assert.IsFalse(files.Read("save.0",8).IsSuccess);Assert.IsFalse(files.Write("save.0",new byte[]{1}).IsSuccess);Assert.AreEqual(1,sdk.Reads);sdk.FailClose=false;Assert.IsTrue(files.RetryCleanup().IsSuccess);Assert.IsFalse(files.CleanupPending);Assert.AreEqual(1,sdk.Reads);Assert.AreEqual(2,sdk.Closes);}
    [Test]public void ProviderCloseRetainsSessionWhileFileCleanupFails(){sdk.FailClose=true;var provider=new WeChatStorageProvider(files);var s=provider.Create(new StorageAddress("test","slot"));Assert.IsFalse(s.ReadAsync().GetAwaiter().GetResult().IsSuccess);var c=new CleanupContext(default,Timeout.InfiniteTimeSpan);Assert.IsFalse(s.CleanupAsync(c).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(1,provider.OpenSessionCount);sdk.FailClose=false;Assert.IsTrue(s.CleanupAsync(c).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(0,provider.OpenSessionCount);Assert.AreEqual(1,sdk.Reads);}
    [Test]public void ClosedSessionCleanupCannotTouchLaterSessionsPendingFd()
    {
        var provider=new WeChatStorageProvider(files);var cleanup=new CleanupContext(default,Timeout.InfiniteTimeSpan);var old=provider.Create(new StorageAddress("test","old"));Assert.IsTrue(old.CleanupAsync(cleanup).GetAwaiter().GetResult().IsSuccess);
        var current=provider.Create(new StorageAddress("test","current"));sdk.FailClose=true;Assert.IsFalse(current.ReadAsync().GetAwaiter().GetResult().IsSuccess);int closes=sdk.Closes;
        Assert.IsTrue(old.CleanupAsync(cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(closes,sdk.Closes);Assert.IsTrue(files.CleanupPending);Assert.AreEqual(1,provider.OpenSessionCount);
        sdk.FailClose=false;Assert.IsTrue(current.CleanupAsync(cleanup).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(0,provider.OpenSessionCount);
    }
    [Test]public void WriteUsesCloneAndExactSdkSuccess(){var b=new byte[]{7};Assert.IsTrue(files.Write("save.0",b).IsSuccess);Assert.AreEqual(7,b[0]);sdk.WriteResult="unknown";Assert.IsFalse(files.Write("save.1",b).IsSuccess);Assert.AreEqual(2,sdk.Writes);}
    [Test]public void MissingRootIsCreatedOnlyDuringWrite(){sdk.RootResult="accessSync:fail no such file or directory";Assert.IsTrue(files.Write("save.0",new byte[0]).IsSuccess);Assert.AreEqual(1,sdk.Mkdirs);Assert.AreEqual(1,sdk.Writes);}
    [Test]public void RootPermissionDoesNotCreateOrWrite(){sdk.RootResult="accessSync:fail permission denied";Assert.AreEqual(ErrorKind.PermissionDenied,files.Write("save.0",new byte[0]).Error.Kind);Assert.AreEqual(0,sdk.Mkdirs+sdk.Writes);}
    [Test]public void MkdirFailureDoesNotWrite(){sdk.RootResult="accessSync:fail no such file or directory";sdk.MkdirResult="mkdirSync:fail quota exceeded";Assert.IsFalse(files.Write("save.0",new byte[0]).IsSuccess);Assert.AreEqual(0,sdk.Writes);}
    [Test]public void InvalidKeyOrBoundNeverEntersSdk(){Assert.IsFalse(files.Read("../bad",8).IsSuccess);Assert.IsFalse(files.Read("ok",65537).IsSuccess);Assert.IsFalse(files.Write("ok",new byte[65537]).IsSuccess);Assert.AreEqual(0,sdk.Opens+sdk.Writes);}
    [Test]public void ReentrantReadNeverClosesOuterFd(){sdk.OnRead=()=>{sdk.OnRead=null;Assert.AreEqual(ErrorKind.InvalidState,files.Read("save.1",8).Error.Kind);};Assert.IsTrue(files.Read("save.0",8).IsSuccess);Assert.AreEqual(1,sdk.Closes);}
    [Test]public void WorkerCallCannotEnterSdk(){Result<StorageReadResult> r=default;var t=new Thread(()=>r=files.Read("save.0",8));t.Start();t.Join();Assert.AreEqual(ErrorKind.InvalidState,r.Error.Kind);Assert.AreEqual(0,sdk.Opens);}
    [TestCase(false)][TestCase(true)]public void UnreturnedFdQuarantinesPortWithoutInventingCleanup(bool empty)
    {
        sdk.EmptyFd=empty;sdk.FailOpen=!empty;var provider=new WeChatStorageProvider(files);var session=provider.Create(new StorageAddress("test","slot"));
        Assert.IsFalse(session.ReadAsync().GetAwaiter().GetResult().IsSuccess);Assert.IsTrue(files.CleanupPending);Assert.IsFalse(files.RetryCleanup().IsSuccess);
        Assert.IsFalse(session.CleanupAsync(new CleanupContext(default,Timeout.InfiniteTimeSpan)).GetAwaiter().GetResult().IsSuccess);Assert.AreEqual(1,provider.OpenSessionCount);
        Assert.IsFalse(files.Read("save.0",8).IsSuccess);Assert.IsFalse(files.Write("save.0",new byte[0]).IsSuccess);Assert.AreEqual(1,sdk.Opens);Assert.AreEqual(0,sdk.Closes+sdk.Reads+sdk.Writes);
    }
    [Test]public void ConfiguredFileBudgetReadsAndWritesAboveLegacyDefault()
    {
        var options=new WeChatStorageOptions(1024*1024);files=new WxStorageFiles.CSharpFiles(sdk,"/private-fixture/root",options);sdk.Size=128*1024;
        Assert.AreEqual(1024*1024,files.MaxFileBytes);Assert.IsTrue(files.Read("save.0",1024*1024).IsSuccess);Assert.AreEqual(1,sdk.Reads);
        Assert.IsTrue(files.Write("save.0",new byte[128*1024]).IsSuccess);Assert.AreEqual(1,sdk.Writes);
        Assert.Throws<ArgumentException>(()=>new WeChatStorageProvider(new WxStorageFiles.CSharpFiles(sdk,"/private-fixture/root"),options));
        sdk.Size=1024*1024+1;Assert.IsFalse(files.Read("save.0",1024*1024).IsSuccess);Assert.AreEqual(1,sdk.Reads);
    }
}
