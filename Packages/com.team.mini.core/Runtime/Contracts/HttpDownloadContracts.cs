using System;
using System.Linq;

namespace MiniFramework.Contracts
{
    /// <summary>Envelope for fully buffered bodies, not a platform request limit.</summary>
    public static class HttpBufferLimits
    {
        public const int MaxSupportedBytes=16*1024*1024;
        public const int DefaultResponseBytes=1024*1024;
        public const int DefaultUploadBytes=256*1024;
    }
    /// <summary>Known object download. Size and SHA must come from the caller's trusted manifest.</summary>
    public sealed class HttpDownloadRequest
    {
        public HttpRequestSpec Request {get;}
        public string TargetId {get;}
        public long ExpectedBytes {get;}
        public long MaxBytes {get;}
        public string ExpectedSha256 {get;}
        public int BufferBytes {get;}
        public HttpDownloadRequest(HttpRequestSpec request,string targetId,long expectedBytes,string expectedSha256,long? maxBytes=null,int bufferBytes=16*1024)
        {
            if(request==null||request.Verb!=HttpVerb.Get||!StorageIdentifier.IsValid(targetId)||expectedBytes<0||
                expectedSha256==null||expectedSha256.Length!=64||expectedSha256.Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f'))||
                (maxBytes??expectedBytes)<expectedBytes||(maxBytes??expectedBytes)<0||bufferBytes<1024||bufferBytes>1024*1024)
                throw new ArgumentException("Bounded GET download with exact size and SHA-256 required.");
            Request=request;TargetId=targetId;ExpectedBytes=expectedBytes;ExpectedSha256=expectedSha256;
            MaxBytes=maxBytes??expectedBytes;BufferBytes=bufferBytes;
        }
    }
    public sealed class HttpDownloadReceipt
    {
        public string TargetId {get;} public long Bytes {get;} public string Sha256 {get;}
        public HttpDownloadReceipt(string targetId,long bytes,string sha256)
        {
            if(!StorageIdentifier.IsValid(targetId)||bytes<0||sha256==null||sha256.Length!=64||sha256.Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f')))throw new ArgumentException();
            TargetId=targetId;Bytes=bytes;Sha256=sha256;
        }
    }
    public interface IHttpDownloadClient
    {
        Result<IHttpOperation> CaptureDownload(HttpDownloadRequest request);
    }
    public interface IHttpDownloadTransport
    {
        // Cold factory; no file creation or network IO until SendAsync.
        IHttpDownloadExchange OpenDownload(HttpDownloadRequest request);
    }
    public interface IHttpDownloadExchange:IHttpExchange
    {
        // Called only after successful actual terminal transfer and observer/owner checks.
        // Never overwrite an existing target. Failure may require explicit reconciliation.
        Result Publish();
    }
    public interface IHttpDownloadStore
    {
        Result<IHttpDownloadFile> Create(HttpDownloadRequest request);
    }
    public interface IHttpDownloadFile
    {
        // Consume only count bytes synchronously; never retain the network-owned buffer.
        Result Append(byte[] buffer,int count);
        Result Seal();
        Result Publish();
        // Idempotent; release handles and discard only owned unpublished staging data.
        // Failure retains ownership for an explicit cleanup retry. Never delete a published target.
        Result Cleanup();
    }
}
