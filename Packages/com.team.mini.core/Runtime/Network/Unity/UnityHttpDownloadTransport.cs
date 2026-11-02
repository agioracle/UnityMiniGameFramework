using System;
using System.Linq;
using System.Security.Cryptography;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using UnityEngine.Networking;

namespace MiniFramework.Network.Unity
{
    /// <summary>Incremental C# UWR download. Application buffers are bounded; vendor SDK buffering is not promised.</summary>
    public sealed class UnityHttpDownloadTransport:IHttpDownloadTransport
    {
        readonly IHttpDownloadStore store;
        public UnityHttpDownloadTransport(IHttpDownloadStore store){this.store=store??throw new ArgumentNullException(nameof(store));}
        public IHttpDownloadExchange OpenDownload(HttpDownloadRequest request)
        {if(request==null)throw new ArgumentNullException(nameof(request));return new Exchange(store,request);}
        static FrameworkError Error(string code,ErrorKind kind=ErrorKind.Integrity)=>new FrameworkError(kind,"network.download."+code);
        sealed class Writer:DownloadHandlerScript
        {
            readonly IHttpDownloadFile file;readonly HttpDownloadRequest spec;readonly SHA256 hash=SHA256.Create();
            public long Bytes {get;private set;} public FrameworkError? Failure {get;private set;}
            public Writer(IHttpDownloadFile file,HttpDownloadRequest spec):base(new byte[spec.BufferBytes]){this.file=file;this.spec=spec;}
            protected override void ReceiveContentLengthHeader(ulong length)
            {if(length>(ulong)spec.MaxBytes||length>(ulong)spec.ExpectedBytes)Failure=Error("limit");}
            protected override bool ReceiveData(byte[] data,int count)
            {
                if(Failure.HasValue)return false;
                if(count==0)return true;
                if(data==null||count<0||count>data.Length||count>spec.BufferBytes||count>spec.MaxBytes-Bytes||count>spec.ExpectedBytes-Bytes)
                {Failure=Error("limit");return false;}
                try
                {
                    var written=file.Append(data,count);if(!written.IsSuccess){Failure=written.Error;return false;}
                    hash.TransformBlock(data,0,count,null,0);Bytes+=count;return true;
                }
                catch(Exception){Failure=Error("write",ErrorKind.Provider);return false;}
            }
            public string CompleteHash(){hash.TransformFinalBlock(Array.Empty<byte>(),0,0);return BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant();}
            public void ReleaseHash()=>hash.Dispose();
        }
        sealed class Exchange:IHttpDownloadExchange
        {
            readonly IHttpDownloadStore store;readonly HttpDownloadRequest spec;
            UnityWebRequest request;Writer writer;IHttpDownloadFile file;
            bool sent,started,ended,aborted,closed,verified,published;
            public Exchange(IHttpDownloadStore store,HttpDownloadRequest spec){this.store=store;this.spec=spec;}
            public async UniTask<HttpTransportResult> SendAsync()
            {
                if(sent||closed)return Fail("state",ErrorKind.InvalidState);sent=true;
                if(aborted){ended=true;return Fail("aborted",ErrorKind.Canceled);}
                try
                {
                    var opened=store.Create(spec);if(!opened.IsSuccess){ended=true;return HttpTransportResult.Failed(opened.Error);}
                    file=opened.Value;if(file==null){ended=true;return Fail("file_null",ErrorKind.Provider);}
                    request=new UnityWebRequest(spec.Request.Uri.AbsoluteUri,"GET");request.redirectLimit=0;request.timeout=0;
                    request.disposeDownloadHandlerOnDispose=true;
                    writer=new Writer(file,spec);request.downloadHandler=writer;
                    foreach(var header in spec.Request.Headers)request.SetRequestHeader(header.Name,header.Value);
                    _ = request.SendWebRequest();started=true;
                }
                catch(Exception){ended=true;return Fail("start",ErrorKind.Provider);}
                while(!request.isDone)await UniTask.Yield();ended=true;
                if(aborted)return Fail("aborted",ErrorKind.Canceled);
                if(writer.Failure.HasValue)return HttpTransportResult.Failed(writer.Failure.Value);
                if(request.result!=UnityWebRequest.Result.Success)
                    return Fail(request.result==UnityWebRequest.Result.ConnectionError?"connection":"transfer",ErrorKind.Network);
                if(request.responseCode<200||request.responseCode>=300)return Fail("http",ErrorKind.Network);
                try
                {
                    var sha=writer.CompleteHash();
                    if(writer.Bytes!=spec.ExpectedBytes||sha!=spec.ExpectedSha256)return Fail("integrity");
                    var sealedFile=file.Seal();if(!sealedFile.IsSuccess)return HttpTransportResult.Failed(sealedFile.Error);
                    var headers=request.GetResponseHeaders();var rows=headers==null?Array.Empty<HttpHeader>():headers.Select(p=>new HttpHeader(p.Key,p.Value)).ToArray();
                    var response=HttpResponse.FromDownload((int)request.responseCode,new HttpDownloadReceipt(spec.TargetId,writer.Bytes,sha),rows);
                    verified=true;return HttpTransportResult.Received(response);
                }
                catch(Exception){return Fail("verify",ErrorKind.Provider);}
            }
            static HttpTransportResult Fail(string code,ErrorKind kind=ErrorKind.Integrity)=>HttpTransportResult.Failed(Error(code,kind));
            public Result Publish()
            {
                if(!verified||aborted||closed||!ended)return Result.Failure(Error("publish_state",ErrorKind.InvalidState));
                if(published)return Result.Success();
                var r=file.Publish();if(r.IsSuccess)published=true;return r;
            }
            public Result Abort(){if(!closed&&!ended){aborted=true;request?.Abort();}return Result.Success();}
            public UniTask<Result> CleanupAsync(CleanupContext cleanup)
            {
                if(closed)return UniTask.FromResult(Result.Success());
                if(started&&!ended&&request!=null&&!request.isDone)return UniTask.FromResult(Result.Failure(Error("running",ErrorKind.CleanupFailed)));
                try
                {
                    request?.Dispose();request=null;writer?.ReleaseHash();writer=null;
                    if(file!=null){var r=file.Cleanup();if(!r.IsSuccess)return UniTask.FromResult(r);file=null;}
                    closed=true;return UniTask.FromResult(Result.Success());
                }
                catch(Exception){return UniTask.FromResult(Result.Failure(Error("cleanup",ErrorKind.CleanupFailed)));}
            }
        }
    }
}
