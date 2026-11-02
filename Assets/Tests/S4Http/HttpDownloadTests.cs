using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Network;
using MiniFramework.Network.Unity;
using MiniFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class HttpDownloadTests
{
    [Serializable] sealed class Info{public string baseUrl;}
    static OperationContext Wait=>new OperationContext(default,TimeSpan.FromSeconds(20));
    static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);
    readonly List<GameContext> contexts=new List<GameContext>();string root,url;
    [SetUp] public void Setup(){url=JsonUtility.FromJson<Info>(File.ReadAllText("Library/S403HttpServer.json")).baseUrl;root=Path.Combine(Path.GetTempPath(),"mini-download-"+Guid.NewGuid().ToString("N"));}
    [UnityTearDown] public IEnumerator Close()=>UniTask.ToCoroutine(async()=>{foreach(var c in contexts)Assert.IsTrue((await c.CloseAsync(Wait)).IsSuccess);contexts.Clear();if(Directory.Exists(root))Directory.Delete(root,true);});
    static string Sha(byte[] b){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
    static string Expected(long size)
    {using(var h=SHA256.Create()){var chunk=Enumerable.Repeat((byte)'x',65536).ToArray();while(size>0){int n=(int)Math.Min(size,chunk.Length);h.TransformBlock(chunk,0,n,null,0);size-=n;}h.TransformFinalBlock(Array.Empty<byte>(),0,0);return BitConverter.ToString(h.Hash).Replace("-","").ToLowerInvariant();}}
    HttpDownloadRequest Spec(string kind="body",long bytes=2*1024*1024,string sha=null,string target="model")=>new HttpDownloadRequest(new HttpRequestSpec(HttpVerb.Get,url+"/limits/"+Guid.NewGuid().ToString("N")+"/"+kind),target,bytes,sha??Expected(bytes));
    async UniTask<HttpClientService> New(IHttpDownloadStore store)
    {
        var c=new HttpClientService(new UnityHttpTransport(),downloadTransport:new UnityHttpDownloadTransport(store));
        var context=new GameContext(new[]{new ModuleRegistration("network",Array.Empty<string>(),()=>c,typeof(IHttpClient),typeof(IHttpDownloadClient))},Cleanup);contexts.Add(context);Assert.IsTrue((await context.StartAsync(Wait)).IsSuccess);return c;
    }
    static async UniTask Drain(HttpClientService c){for(int i=0;i<1000&&c.PendingCount>0;i++)await UniTask.Yield();Assert.AreEqual(0,c.PendingCount);Assert.AreEqual(0,c.ActiveExchangeCount);Assert.AreEqual(0,c.RunningRequestCount);}
    int Parts()=>Directory.Exists(root)?Directory.GetFiles(root,"*.part").Length:0;
    [UnityTest] public IEnumerator LargerThanBufferedEnvelopeDownloadsToFile()=>UniTask.ToCoroutine(async()=>
    {
        var store=new LocalHttpDownloadStore(root,32L*1024*1024);var c=await New(store);var spec=Spec("large",20L*1024*1024);
        var op=c.CaptureDownload(spec).Value;var result=await op.WaitAsync(Wait);Assert.IsTrue(result.IsSuccess,result.IsSuccess?"":result.Error.Code);
        Assert.AreEqual(0,result.Value.BodyLength);Assert.AreEqual(spec.ExpectedBytes,result.Value.Download.Bytes);Assert.Throws<InvalidOperationException>(()=>result.Value.CopyBody());
        using(var stream=File.OpenRead(store.GetPublishedPath("model")))using(var h=SHA256.Create())Assert.AreEqual(spec.ExpectedSha256,BitConverter.ToString(h.ComputeHash(stream)).Replace("-","").ToLowerInvariant());
        Assert.AreEqual(1,op.Attempts);Assert.AreEqual(0,Parts());await Drain(c);
    });
    [UnityTest] public IEnumerator HashMismatchDiscardsStageWithoutPublishingOrRetry()=>UniTask.ToCoroutine(async()=>
    {var store=new LocalHttpDownloadStore(root,4*1024*1024);var c=await New(store);var op=c.CaptureDownload(Spec(sha:new string('0',64))).Value;Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1,op.Attempts);Assert.IsNull(op.Response);Assert.IsFalse(File.Exists(store.GetPublishedPath("model")));Assert.AreEqual(0,Parts());await Drain(c);});
    [UnityTest] public IEnumerator DeclaredOversizeCannotPublish()=>UniTask.ToCoroutine(async()=>
    {var store=new LocalHttpDownloadStore(root,4*1024*1024);var c=await New(store);var op=c.CaptureDownload(Spec(bytes:1024)).Value;Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1,op.Attempts);Assert.AreEqual(0,Parts());await Drain(c);});
    [UnityTest] public IEnumerator UnknownContentLengthCannotBypassLimit()=>UniTask.ToCoroutine(async()=>
    {var store=new LocalHttpDownloadStore(root,4*1024*1024);var c=await New(store);var op=c.CaptureDownload(Spec("chunked",1024)).Value;Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(0,Parts());Assert.IsNull(op.Response);await Drain(c);});
    [UnityTest] public IEnumerator TruncationNeverPublishes()=>UniTask.ToCoroutine(async()=>
    {var store=new LocalHttpDownloadStore(root,4*1024*1024);var c=await New(store);var op=c.CaptureDownload(Spec("truncated")).Value;Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1,op.Attempts);Assert.AreEqual(0,Parts());await Drain(c);});
    [UnityTest] public IEnumerator CancellationAbortsAndEventuallyDiscardsStage()=>UniTask.ToCoroutine(async()=>
    {var store=new LocalHttpDownloadStore(root,4*1024*1024);var c=await New(store);using(var token=new CancellationTokenSource()){var op=c.CaptureDownload(Spec("slow")).Value;var task=op.WaitAsync(OperationContext.Unlimited(token.Token));await UniTask.Yield();token.Cancel();Assert.AreEqual(ErrorKind.Canceled,(await task).Error.Kind);await Drain(c);Assert.IsFalse(File.Exists(store.GetPublishedPath("model")));Assert.AreEqual(0,Parts());}});
    [UnityTest] public IEnumerator OwnerCloseDuringDownloadNeverPublishes()=>UniTask.ToCoroutine(async()=>
    {var store=new LocalHttpDownloadStore(root,4*1024*1024);var c=await New(store);var op=c.CaptureDownload(Spec("slow")).Value;var task=op.WaitAsync(Wait);await UniTask.Yield();Assert.IsTrue((await contexts[0].CloseAsync(Wait)).IsSuccess);Assert.AreEqual(ErrorKind.Canceled,(await task).Error.Kind);Assert.AreEqual(0,c.ActiveExchangeCount);Assert.AreEqual(0,Parts());Assert.IsFalse(File.Exists(store.GetPublishedPath("model")));});
    [UnityTest] public IEnumerator DeadlineAbortsWithoutPublishing()=>UniTask.ToCoroutine(async()=>
    {var store=new LocalHttpDownloadStore(root,4*1024*1024);var c=await New(store);var op=c.CaptureDownload(Spec("slow")).Value;Assert.AreEqual(ErrorKind.Timeout,(await op.WaitAsync(new OperationContext(default,TimeSpan.FromMilliseconds(50)))).Error.Kind);await Drain(c);Assert.AreEqual(0,Parts());Assert.IsFalse(File.Exists(store.GetPublishedPath("model")));});
    [UnityTest] public IEnumerator ExistingTargetIsNeverOverwritten()=>UniTask.ToCoroutine(async()=>
    {var store=new LocalHttpDownloadStore(root,4*1024*1024);Directory.CreateDirectory(root);var path=store.GetPublishedPath("model");File.WriteAllBytes(path,new byte[]{7,9});var c=await New(store);Assert.IsFalse((await c.CaptureDownload(Spec()).Value.WaitAsync(Wait)).IsSuccess);CollectionAssert.AreEqual(new byte[]{7,9},File.ReadAllBytes(path));Assert.AreEqual(0,Parts());});
    [UnityTest] public IEnumerator StorageBudgetRejectsBeforeTransferAndPreservesOtherFiles()=>UniTask.ToCoroutine(async()=>
    {var store=new LocalHttpDownloadStore(root,1024);Directory.CreateDirectory(root);File.WriteAllBytes(Path.Combine(root,"old.part"),new byte[1000]);var c=await New(store);var op=c.CaptureDownload(Spec()).Value;Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1000,new FileInfo(Path.Combine(root,"old.part")).Length);Assert.AreEqual(1,op.Attempts);await Drain(c);});
    [UnityTest] public IEnumerator BufferedAggregateBudgetSerializesRequestsAndReleasesReservations()=>UniTask.ToCoroutine(async()=>
    {
        var c=new HttpClientService(new UnityHttpTransport(),new HttpClientLimits(maxResponseBytes:1024,maxBufferedResponseBytesInFlight:1024));
        var context=new GameContext(new[]{new ModuleRegistration("network",Array.Empty<string>(),()=>c,typeof(IHttpClient))},Cleanup);contexts.Add(context);Assert.IsTrue((await context.StartAsync(Wait)).IsSuccess);
        var a=c.Capture(new HttpRequestSpec(HttpVerb.Get,url+"/case/"+Guid.NewGuid().ToString("N")+"/slow")).Value;
        var b=c.Capture(new HttpRequestSpec(HttpVerb.Get,url+"/case/"+Guid.NewGuid().ToString("N")+"/slow")).Value;
        var first=a.WaitAsync(Wait);var second=b.WaitAsync(Wait);await UniTask.Yield();Assert.AreEqual(1,c.ActiveExchangeCount);Assert.AreEqual(1024,c.ReservedResponseBytes);
        Assert.IsTrue((await first).IsSuccess);Assert.IsTrue((await second).IsSuccess);await Drain(c);Assert.AreEqual(0,c.ReservedResponseBytes);
    });
    sealed class FaultStore:IHttpDownloadStore
    {
        readonly IHttpDownloadStore inner;public FaultFile File;
        public FaultStore(IHttpDownloadStore inner){this.inner=inner;}
        public Result<IHttpDownloadFile> Create(HttpDownloadRequest r){var opened=inner.Create(r);if(!opened.IsSuccess)return opened;File=new FaultFile(opened.Value);return Result<IHttpDownloadFile>.Success(File);}
    }
    sealed class FaultFile:IHttpDownloadFile
    {
        readonly IHttpDownloadFile inner;public bool FailCleanup=true;public int Writes;
        public FaultFile(IHttpDownloadFile inner){this.inner=inner;}
        public Result Append(byte[] b,int n){Writes++;return Result.Failure(new FrameworkError(ErrorKind.Storage,"test.disk.full"));}
        public Result Seal()=>inner.Seal();public Result Publish()=>inner.Publish();
        public Result Cleanup()=>FailCleanup?Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"test.cleanup")):inner.Cleanup();
    }
    [UnityTest] public IEnumerator FailedCleanupRetainsLeaseUntilExplicitContextRetry()=>UniTask.ToCoroutine(async()=>
    {
        var store=new FaultStore(new LocalHttpDownloadStore(root,4*1024*1024));var c=await New(store);var op=c.CaptureDownload(Spec()).Value;Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1,c.ActiveExchangeCount);
        Assert.IsFalse((await contexts[0].CloseAsync(Wait)).IsSuccess);int writes=store.File.Writes;store.File.FailCleanup=false;
        Assert.IsTrue((await contexts[0].RetryCleanupAsync(Cleanup,Wait)).IsSuccess);Assert.AreEqual(writes,store.File.Writes);Assert.AreEqual(0,c.ActiveExchangeCount);Assert.AreEqual(1,op.Attempts);
    });
    [Test] public void RequestRejectsTraversalInvalidHashAndUnboundedBuffers()
    {Assert.Throws<ArgumentException>(()=>new HttpDownloadRequest(new HttpRequestSpec(HttpVerb.Get,"https://example.test/"),"../bad",0,new string('0',64)));Assert.Throws<ArgumentException>(()=>new HttpDownloadRequest(new HttpRequestSpec(HttpVerb.Get,"https://example.test/"),"model",10,"bad"));Assert.Throws<ArgumentException>(()=>new HttpDownloadRequest(new HttpRequestSpec(HttpVerb.Get,"https://example.test/"),"model",10,new string('0',64),bufferBytes:int.MaxValue));}
}
