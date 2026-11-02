using System;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using UnityEngine.Networking;
namespace MiniFramework.Network.Unity
{
    public sealed class UnityHttpTransport:IHttpTransport
    {
        public IHttpExchange Open(HttpRequestSpec request,int maxResponseBytes)
        {if(request==null||maxResponseBytes<1||maxResponseBytes>HttpBufferLimits.MaxSupportedBytes)throw new ArgumentException();return new Exchange(request,maxResponseBytes);}
        sealed class BoundedDownload:DownloadHandlerScript
        {
            readonly MemoryStream stream=new MemoryStream();readonly int limit;readonly bool useLength;public bool TooLarge {get;private set;}
            internal BoundedDownload(int limit,bool useLength):base(new byte[8192]){this.limit=limit;this.useLength=useLength;}
            protected override void ReceiveContentLengthHeader(ulong length){if(useLength&&length>(ulong)limit)TooLarge=true;}
            protected override bool ReceiveData(byte[] data,int length)
            {if(TooLarge)return false;if(data==null||length<=0)return true;if(length>limit-stream.Length){TooLarge=true;return false;}stream.Write(data,0,length);return true;}
            internal byte[] Copy()=>stream.ToArray();internal void ReleaseBuffer()=>stream.Dispose();
        }
        sealed class Exchange:IHttpExchange
        {
            readonly HttpRequestSpec spec;readonly int limit;UnityWebRequest request;BoundedDownload download;
            bool sent,nativeStarted,aborted,completed,closed;
            internal Exchange(HttpRequestSpec spec,int limit){this.spec=spec;this.limit=limit;}
            public async UniTask<HttpTransportResult> SendAsync()
            {
                if(sent||closed)return Failed("network.transport.state",ErrorKind.InvalidState);sent=true;
                if(aborted){completed=true;return Failed("network.transport.aborted",ErrorKind.Provider);}
                request=new UnityWebRequest(spec.Uri.AbsoluteUri,spec.Verb.ToString().ToUpperInvariant());
                request.redirectLimit=0;request.timeout=0;request.disposeDownloadHandlerOnDispose=true;request.disposeUploadHandlerOnDispose=true;
                download=new BoundedDownload(limit,spec.Verb!=HttpVerb.Head);request.downloadHandler=download;
                if(spec.Verb!=HttpVerb.Get&&spec.Verb!=HttpVerb.Head)request.uploadHandler=new UploadHandlerRaw(spec.CopyBody());
                foreach(var header in spec.Headers)request.SetRequestHeader(header.Name,header.Value);
                try{request.SendWebRequest();nativeStarted=true;}catch(Exception){completed=true;return Failed("network.transport.start",ErrorKind.Provider);}
                while(!request.isDone)await UniTask.Yield();completed=true;
                if(aborted)return Failed("network.transport.aborted",ErrorKind.Provider);
                if(download.TooLarge)return Failed("network.response.limit",ErrorKind.Integrity);
                if(request.responseCode>=200&&request.responseCode<300&&request.result!=UnityWebRequest.Result.Success)
                    return request.result==UnityWebRequest.Result.DataProcessingError?Failed("network.transport.data",ErrorKind.Integrity):HttpTransportResult.Failed(new FrameworkError(ErrorKind.Network,"network.transport.connection"),true);
                if(request.responseCode>=100&&request.responseCode<=599)
                {
                    try
                    {
                        var headers=request.GetResponseHeaders();var rows=headers==null?Array.Empty<HttpHeader>():headers.Select(p=>new HttpHeader(p.Key,p.Value)).ToArray();
                        return HttpTransportResult.Received(new HttpResponse((int)request.responseCode,download.Copy(),rows));
                    }
                    catch(ArgumentException){return Failed("network.response.headers",ErrorKind.Integrity);}
                }
                return HttpTransportResult.Failed(new FrameworkError(ErrorKind.Network,"network.transport.connection"),request.result==UnityWebRequest.Result.ConnectionError);
            }
            static HttpTransportResult Failed(string code,ErrorKind kind)=>HttpTransportResult.Failed(new FrameworkError(kind,code));
            public Result Abort(){if(closed||completed)return Result.Success();aborted=true;request?.Abort();return Result.Success();}
            public UniTask<Result> CleanupAsync(CleanupContext cleanup)
            {
                if(closed)return UniTask.FromResult(Result.Success());
                if(nativeStarted&&!completed&&request!=null&&!request.isDone)return UniTask.FromResult(Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"network.cleanup.running")));
                request?.Dispose();download?.ReleaseBuffer();request=null;download=null;closed=true;return UniTask.FromResult(Result.Success());
            }
        }
    }
}
