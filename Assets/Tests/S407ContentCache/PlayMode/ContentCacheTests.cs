using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.S407ContentCache;
using MiniFramework.Providers.WeChat.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;
namespace MiniFramework.S407ContentCache.Tests
{
    public sealed class ContentCacheTests
    {
        sealed class Files : IContentCacheFiles, IWeChatStorageFiles
        {
            public readonly Dictionary<string,byte[]> Bytes = new Dictionary<string,byte[]>(); public int Writes;
            public StoragePersistence Persistence => StoragePersistence.Volatile;
            public Result<StorageReadResult> Read(string id,int max) => Result<StorageReadResult>.Success(Bytes.TryGetValue(id,out var b)?new StorageReadResult(true,b):new StorageReadResult(false));
            public Result Write(string id,byte[] b) { Writes++;Bytes[id]=(byte[])b.Clone();return Result.Success(); }
            public Result WriteOwnedCopy(string id,byte[] b) => Write(id,b);
            public Result DeleteOwnedCopy(string id) { Bytes.Remove(id);return Result.Success(); }
        }
        sealed class Transfer : IHttpTransport,IHttpDownloadTransport
        {
            readonly Files files;readonly byte[] bytes;
            public Transfer(Files files,byte[] bytes) {this.files=files;this.bytes=bytes;}
            public IHttpExchange Open(HttpRequestSpec r,int max) => throw new InvalidOperationException("no buffered request");
            public IHttpDownloadExchange OpenDownload(HttpDownloadRequest r) => new Exchange(files,bytes,r);
            sealed class Exchange:IHttpDownloadExchange
            {
                readonly Files files;readonly byte[] bytes;readonly HttpDownloadRequest r;
                public Exchange(Files f,byte[] b,HttpDownloadRequest r){files=f;bytes=b;this.r=r;}
                public UniTask<HttpTransportResult> SendAsync() => UniTask.FromResult(HttpTransportResult.Received(HttpResponse.FromDownload(200,new HttpDownloadReceipt(r.TargetId,bytes.Length,ContentCacheScenario.Sha(bytes)))));
                public Result Publish()=>files.Write(r.TargetId,bytes);public Result Abort()=>Result.Success();
                public UniTask<Result> CleanupAsync(CleanupContext c)=>UniTask.FromResult(Result.Success());
            }
        }
        Files files;Transfer transfer;ContentDescriptor descriptor;
        void Setup(){files=new Files();var bytes=Enumerable.Range(1,64).Select(x=>(byte)x).ToArray();transfer=new Transfer(files,bytes);descriptor=new ContentDescriptor("https://fixture.invalid/content",bytes.Length,ContentCacheScenario.Sha(bytes));}
        ContentCacheScenario New(string launch=null)=>new ContentCacheScenario(files,files,transfer,transfer,descriptor,Guid.NewGuid().ToString("N"),launch??Guid.NewGuid().ToString("N"));
        [UnityTest]public IEnumerator SyntheticRuntimeProtocolGatesConsumerAndPreservesOriginalCache()=>UniTask.ToCoroutine(async()=>{
            Setup();var a=New();await a.Execute();Assert.AreEqual("cache_populated",a.Evidence.phase);Assert.AreEqual(1,a.Evidence.cases.Single().consumers);
            var b=New();await b.Execute();Assert.IsTrue(b.Evidence.passed&&b.Evidence.originalCachePreserved&&b.Evidence.crossRuntimeCacheRestored);Assert.AreEqual(4,b.Evidence.cases.Count);Assert.AreEqual(1,b.Evidence.cases[0].consumers);Assert.IsTrue(b.Evidence.cases.Skip(1).All(c=>c.consumers==0));Assert.IsTrue(b.Evidence.cases.All(c=>c.sourceReleasedBeforeCache&&c.actualDownloads==0));
        });
        [UnityTest]public IEnumerator SameRuntimeCannotClaimRestart()=>UniTask.ToCoroutine(async()=>{
            Setup();await New("aabbccddeeff00112233445566778899").Execute();int writes=files.Writes;
            try{await New("aabbccddeeff00112233445566778899").Execute();Assert.Fail();}catch(InvalidOperationException e){Assert.AreEqual("marker.fresh_runtime_required",e.Message);}Assert.AreEqual(writes,files.Writes);
        });
        [UnityTest]public IEnumerator CorruptCheckpointIsPreserved()=>UniTask.ToCoroutine(async()=>{
            Setup();files.Bytes[ContentCacheScenario.Marker]=System.Text.Encoding.UTF8.GetBytes("{}");try{await New().Execute();Assert.Fail();}catch(InvalidOperationException e){Assert.AreEqual("marker.invalid",e.Message);}Assert.AreEqual(0,files.Writes);
        });
        [UnityTest]public IEnumerator ExistingFileWithoutCheckpointIsNeverOverwritten()=>UniTask.ToCoroutine(async()=>{
            Setup();files.Bytes[ContentCacheScenario.Content]=new byte[]{42};try{await New().Execute();Assert.Fail();}catch(InvalidOperationException e){Assert.AreEqual("cache.unowned_existing",e.Message);}Assert.AreEqual(0,files.Writes);Assert.AreEqual(42,files.Bytes[ContentCacheScenario.Content][0]);
        });
    }
}
