using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public enum HttpVerb { Get, Head, Post, Put, Patch, Delete }
    public enum HttpOperationState { Captured, Queued, Sending, BackingOff, Succeeded, Failed, Canceled, TimedOut }
    public readonly struct HttpHeader
    {
        public string Name {get;}public string Value {get;}
        public HttpHeader(string name,string value)
        {
            if(string.IsNullOrEmpty(name)||name.Length>128||value==null||value.Length>8192||name.Any(c=>!(c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||"!#$%&'*+-.^_`|~".Contains(c)))||value.Any(c=>c<32||c==127))throw new ArgumentException("HTTP header invalid.");
            Name=name;Value=value;
        }
    }
    public sealed class HttpRequestSpec
    {
        readonly byte[] body;public HttpVerb Verb {get;}public Uri Uri {get;}public IReadOnlyList<HttpHeader> Headers {get;}
        public int BodyLength=>body.Length;
        public HttpRequestSpec(HttpVerb verb,string uri,byte[] body=null,IEnumerable<HttpHeader> headers=null)
        {
            if(!Enum.IsDefined(typeof(HttpVerb),verb)||uri==null||uri.Length>4096||!System.Uri.TryCreate(uri,UriKind.Absolute,out var parsed)||(parsed.Scheme!="http"&&parsed.Scheme!="https")||parsed.UserInfo.Length>0||parsed.Fragment.Length>0)throw new ArgumentException("Absolute HTTP(S) URI without user-info or fragment required.");
            var items=(headers??Array.Empty<HttpHeader>()).ToArray();
            if(items.Length>32||items.Any(h=>string.IsNullOrEmpty(h.Name)||h.Value==null)||items.Sum(h=>Encoding.UTF8.GetByteCount(h.Name)+Encoding.UTF8.GetByteCount(h.Value))>16384||items.Select(h=>h.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=items.Length)throw new ArgumentException("Bounded unique headers required.");
            var reserved=new[]{"Host","Content-Length","Connection","Transfer-Encoding","Expect"};if(items.Any(h=>reserved.Contains(h.Name,StringComparer.OrdinalIgnoreCase)))throw new ArgumentException("Transport owns framing headers.");
            if((verb==HttpVerb.Get||verb==HttpVerb.Head)&&(body?.Length??0)>0)throw new ArgumentException("Read methods have no upload body.");
            if((body?.Length??0)>HttpBufferLimits.MaxSupportedBytes)throw new ArgumentException("Upload exceeds buffered implementation envelope.");
            Verb=verb;Uri=parsed;this.body=body==null?Array.Empty<byte>():(byte[])body.Clone();Headers=Array.AsReadOnly(items);
        }
        public byte[] CopyBody()=>(byte[])body.Clone();
    }
    public sealed class HttpResponse
    {
        readonly byte[] body;public int StatusCode {get;}public IReadOnlyList<HttpHeader> Headers {get;}public int BodyLength=>body.Length;
        public HttpDownloadReceipt Download {get;private set;}
        public HttpResponse(int statusCode,byte[] body,IEnumerable<HttpHeader> headers=null)
        {
            if(statusCode<100||statusCode>599||body==null||body.Length>HttpBufferLimits.MaxSupportedBytes)throw new ArgumentException("Response invalid.");
            var items=(headers??Array.Empty<HttpHeader>()).ToArray();if(items.Length>64||items.Any(h=>string.IsNullOrEmpty(h.Name)||h.Value==null)||items.Sum(h=>Encoding.UTF8.GetByteCount(h.Name)+Encoding.UTF8.GetByteCount(h.Value))>32768)throw new ArgumentException("Response headers invalid.");
            StatusCode=statusCode;this.body=(byte[])body.Clone();Headers=Array.AsReadOnly(items);
        }
        public byte[] CopyBody(){if(Download!=null)throw new InvalidOperationException("Download response contains a file receipt, not buffered bytes.");return (byte[])body.Clone();}
        public static HttpResponse FromDownload(int statusCode,HttpDownloadReceipt receipt,System.Collections.Generic.IEnumerable<HttpHeader> headers=null)
        {if(receipt==null)throw new ArgumentNullException(nameof(receipt));return new HttpResponse(statusCode,Array.Empty<byte>(),headers){Download=receipt};}
        public string Header(string name)=>Headers.FirstOrDefault(h=>string.Equals(h.Name,name,StringComparison.OrdinalIgnoreCase)).Value;
    }
    public sealed class HttpTransportResult
    {
        public HttpResponse Response {get;}public FrameworkError? Error {get;}public bool CanRetry {get;}
        HttpTransportResult(HttpResponse response,FrameworkError? error,bool canRetry){Response=response;Error=error;CanRetry=canRetry;}
        public static HttpTransportResult Received(HttpResponse response)=>new HttpTransportResult(response??throw new ArgumentNullException(nameof(response)),null,false);
        public static HttpTransportResult Failed(FrameworkError error,bool canRetry=false){if(error.Kind==ErrorKind.None)throw new ArgumentException();return new HttpTransportResult(null,error,canRetry);}
    }
    public interface IHttpTransport
    {
        // Cold factory. The client owns this exchange before Send can start.
        IHttpExchange Open(HttpRequestSpec request,int maxResponseBytes);
    }
    public interface IHttpExchange
    {
        UniTask<HttpTransportResult> SendAsync(); // Actual terminal completion, not caller cancellation.
        Result Abort(); // Request interruption; success does not prove actual completion.
        UniTask<Result> CleanupAsync(CleanupContext cleanup);
    }
    public interface IHttpOperation
    {
        string Id {get;}HttpOperationState State {get;}int Attempts {get;}HttpResponse Response {get;}
        FrameworkError? Failure {get;}FrameworkError? CleanupError {get;}FrameworkError? AbortError {get;}
        UniTask<Result<HttpResponse>> WaitAsync(OperationContext wait); // Single use, overall real-time budget.
    }
    public interface IHttpClient
    {
        Result<IHttpOperation> Capture(HttpRequestSpec request); // Cold and bounded; no transport activity.
    }
}
