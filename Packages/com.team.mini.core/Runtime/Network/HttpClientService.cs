using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Network
{
    public sealed class HttpClientService:IHttpClient,IHttpDownloadClient,IModule,IAsyncCleanup,IDiagnosticSource
    {
        readonly IHttpTransport transport;readonly HttpClientLimits limits;readonly HttpRetryPolicy retry;readonly CleanupContext releasePolicy;
        readonly IHttpDownloadTransport downloadTransport;
        readonly List<Operation> operations=new List<Operation>();readonly List<Operation> waiting=new List<Operation>();
        bool initialized,stopped,cleanupEntered;int leases;
        long reservedResponseBytes;
        public string Id {get;}public IReadOnlyList<string> Dependencies {get;}
        public int PendingCount=>operations.Count;public int ActiveExchangeCount=>leases;
        public long ReservedResponseBytes=>reservedResponseBytes;
        public int RunningRequestCount=>operations.Count(o=>o.Exchange!=null&&!o.ActualEnded);
        public HttpClientService(IHttpTransport transport,HttpClientLimits limits=null,HttpRetryPolicy retry=null,string id="network",string transportModuleId=null,CleanupContext? releasePolicy=null,IHttpDownloadTransport downloadTransport=null)
        {
            Main();this.transport=transport??throw new ArgumentNullException(nameof(transport));this.limits=limits??new HttpClientLimits();this.retry=retry??new HttpRetryPolicy();
            this.downloadTransport=downloadTransport;
            if(string.IsNullOrWhiteSpace(id)||transportModuleId!=null&&string.IsNullOrWhiteSpace(transportModuleId))throw new ArgumentException();
            Id=id;Dependencies=transportModuleId==null?Array.Empty<string>():new[]{transportModuleId};this.releasePolicy=releasePolicy??new CleanupContext(default,Timeout.InfiniteTimeSpan);
        }
        internal static FrameworkError Error(string code,ErrorKind kind=ErrorKind.Network)=>new FrameworkError(kind,code);
        static void Main(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("HTTP client requires main thread.");}
        static double Elapsed(long start)=>(Stopwatch.GetTimestamp()-start)/(double)Stopwatch.Frequency;
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            Main();if(operation.Cancellation.IsCancellationRequested)return UniTask.FromResult(Result.Failure(Error("network.initialize.canceled",ErrorKind.Canceled)));
            if(operation.Timeout==TimeSpan.Zero)return UniTask.FromResult(Result.Failure(Error("network.initialize.timeout",ErrorKind.Timeout)));
            if(initialized||stopped)return UniTask.FromResult(Result.Failure(Error("network.initialize.state",ErrorKind.InvalidState)));
            var registered=context.Cleanup.Register(this);if(!registered.IsSuccess)return UniTask.FromResult(Result.Failure(registered.Error));initialized=true;return UniTask.FromResult(Result.Success());
        }
        public Result<IHttpOperation> Capture(HttpRequestSpec request)
        {
            Main();if(!initialized||stopped)return Reject("network.not_ready",ErrorKind.NotReady);
            if(request==null||request.BodyLength>limits.MaxUploadBytes)return Reject("network.request.limit",ErrorKind.InvalidArgument);
            if(operations.Count>=limits.MaxOperations)return Reject("network.capacity",ErrorKind.Unavailable);
            var op=new Operation(this,request);operations.Add(op);return Result<IHttpOperation>.Success(op);
        }
        static Result<IHttpOperation> Reject(string code,ErrorKind kind)=>Result<IHttpOperation>.Failure(Error(code,kind));
        public Result<IHttpOperation> CaptureDownload(HttpDownloadRequest request)
        {
            Main();if(!initialized||stopped)return Reject("network.not_ready",ErrorKind.NotReady);
            if(request==null)return Reject("network.download.argument",ErrorKind.InvalidArgument);
            if(downloadTransport==null)return Reject("network.download.unsupported",ErrorKind.NotSupported);
            if(operations.Count>=limits.MaxOperations)return Reject("network.capacity",ErrorKind.Unavailable);
            var op=new Operation(this,request.Request){Download=request};operations.Add(op);return Result<IHttpOperation>.Success(op);
        }
        FrameworkError? Interrupted(Operation op)
        {
            if(stopped||op.Wait.Cancellation.IsCancellationRequested)return Error(stopped?"network.owner.closed":"network.wait.canceled",ErrorKind.Canceled);
            if(op.Wait.Timeout!=Timeout.InfiniteTimeSpan&&Elapsed(op.Start)>=op.Wait.Timeout.TotalSeconds)return Error("network.wait.timeout",ErrorKind.Timeout);
            return null;
        }
        bool Check(Operation op){var error=Interrupted(op);if(error.HasValue)Cancel(op,error.Value);return op.Abandoned;}
        void Cancel(Operation op,FrameworkError error)
        {
            if(op.Done.Task.Status==UniTaskStatus.Pending)
            {op.Abandoned=true;op.Failure=error;op.State=error.Kind==ErrorKind.Timeout?HttpOperationState.TimedOut:HttpOperationState.Canceled;op.Done.TrySetResult(Result<HttpResponse>.Failure(error));}
            if(op.Exchange!=null&&!op.ActualEnded&&!op.AbortSent)
            {
                op.AbortSent=true;try{var aborted=op.Exchange.Abort();if(!aborted.IsSuccess)op.AbortError=aborted.Error;}catch(Exception){op.AbortError=Error("network.abort.exception",ErrorKind.Provider);}
            }
            if(!op.Started){operations.Remove(op);op.Ended.TrySetResult();}
        }
        void Finish(Operation op,Result<HttpResponse> result)
        {
            if(Check(op)||op.Done.Task.Status!=UniTaskStatus.Pending)return;
            op.State=result.IsSuccess?HttpOperationState.Succeeded:HttpOperationState.Failed;op.Failure=result.IsSuccess?(FrameworkError?)null:result.Error;op.Done.TrySetResult(result);
        }
        async UniTask<bool> Release(Operation op,CleanupContext policy)
        {
            Result result;try{result=await op.Exchange.CleanupAsync(policy);await UniTask.SwitchToMainThread();}catch(Exception){await UniTask.SwitchToMainThread();result=Result.Failure(Error("network.cleanup.exception",ErrorKind.CleanupFailed));}
            if(!result.IsSuccess){op.CleanupError=result.Error;return false;}
            op.Exchange=null;op.CleanupError=null;leases--;reservedResponseBytes-=op.ReservedResponseBytes;op.ReservedResponseBytes=0;return true;
        }
        async UniTask Drive(Operation op)
        {
            try
            {
                while(!Check(op))
                {
                    op.State=HttpOperationState.Queued;waiting.Add(op);
                    long reservation=op.Download==null?limits.MaxResponseBytes:0;
                    while(!Check(op)&&(waiting[0]!=op||leases>=limits.MaxConcurrent||reservation>limits.MaxBufferedResponseBytesInFlight-reservedResponseBytes))await UniTask.Yield();
                    waiting.Remove(op);if(op.Abandoned)break;
                    HttpTransportResult result;
                    try
                    {
                        // Operation.Ended already exists, so shutdown during even a faulty factory retains ownership.
                        op.Exchange=op.Download==null?transport.Open(op.Request,limits.MaxResponseBytes):downloadTransport.OpenDownload(op.Download);if(op.Exchange==null)throw new InvalidOperationException();leases++;
                        op.ReservedResponseBytes=reservation;reservedResponseBytes+=reservation;
                        op.ActualEnded=false;op.AbortSent=false;
                        if(Check(op)){op.ActualEnded=true;result=HttpTransportResult.Failed(Error("network.wait.canceled",ErrorKind.Canceled));}
                        else{op.Attempts++;op.State=HttpOperationState.Sending;result=await op.Exchange.SendAsync();await UniTask.SwitchToMainThread();op.ActualEnded=true;}
                    }
                    catch(Exception){await UniTask.SwitchToMainThread();op.ActualEnded=true;result=HttpTransportResult.Failed(Error("network.transport.exception",ErrorKind.Provider));}
                    Check(op);
                    if(result?.Response!=null&&result.Response.BodyLength>limits.MaxResponseBytes)result=HttpTransportResult.Failed(Error("network.response.limit",ErrorKind.Integrity));
                    if(result?.Response!=null)
                    {
                        var receipt=result.Response.Download;
                        if(op.Download==null&&receipt!=null||op.Download!=null&&(receipt==null||receipt.TargetId!=op.Download.TargetId||receipt.Bytes!=op.Download.ExpectedBytes||receipt.Bytes>op.Download.MaxBytes||receipt.Sha256!=op.Download.ExpectedSha256))
                            result=HttpTransportResult.Failed(Error("network.download.receipt",ErrorKind.Integrity));
                    }
                    var success=result?.Response!=null&&result.Response.StatusCode>=200&&result.Response.StatusCode<300;
                    if(success&&op.Download!=null&&!op.Abandoned)
                    {
                        Result published;
                        try{published=((IHttpDownloadExchange)op.Exchange).Publish();}
                        catch(Exception){published=Result.Failure(Error("network.download.publish",ErrorKind.Provider));}
                        if(!published.IsSuccess){result=HttpTransportResult.Failed(published.Error);success=false;}
                    }
                    if(!op.Abandoned&&result?.Response!=null)op.Response=result.Response;
                    if(op.Exchange!=null&&!await Release(op,releasePolicy))
                    {Finish(op,Result<HttpResponse>.Failure(success?op.CleanupError.Value:HttpRetryPolicy.Classify(result)));return;}
                    if(Check(op))break;
                    if(success){Finish(op,Result<HttpResponse>.Success(result.Response));break;}
                    if(op.Download!=null||!retry.TryDelay(op.Request.Verb,op.Attempts,result,DateTimeOffset.UtcNow,out var delay))
                    {Finish(op,Result<HttpResponse>.Failure(HttpRetryPolicy.Classify(result)));break;}
                    op.State=HttpOperationState.BackingOff;var begin=Stopwatch.GetTimestamp();while(!Check(op)&&Elapsed(begin)<delay.TotalSeconds)await UniTask.Yield();
                }
            }
            catch(Exception){Finish(op,Result<HttpResponse>.Failure(Error("network.driver.exception",ErrorKind.Unexpected)));}
            finally{waiting.Remove(op);if(op.Exchange==null)operations.Remove(op);op.Ended.TrySetResult();}
        }
        sealed class Operation:IHttpOperation
        {
            readonly HttpClientService owner;internal readonly HttpRequestSpec Request;internal readonly UniTaskCompletionSource<Result<HttpResponse>> Done=new UniTaskCompletionSource<Result<HttpResponse>>();
            internal readonly UniTaskCompletionSource Ended=new UniTaskCompletionSource();internal IHttpExchange Exchange;internal bool Started,Abandoned,AbortSent,ActualEnded;
            internal OperationContext Wait;internal long Start;internal UniTask Driver;
            internal HttpDownloadRequest Download;
            internal long ReservedResponseBytes;
            public string Id {get;}=Guid.NewGuid().ToString("N");public HttpOperationState State {get;internal set;}=HttpOperationState.Captured;public int Attempts {get;internal set;}
            public HttpResponse Response {get;internal set;}public FrameworkError? Failure {get;internal set;}public FrameworkError? CleanupError {get;internal set;}public FrameworkError? AbortError {get;internal set;}
            internal Operation(HttpClientService owner,HttpRequestSpec request){this.owner=owner;Request=request;}
            public async UniTask<Result<HttpResponse>> WaitAsync(OperationContext wait)
            {
                Main();if(Started)return Result<HttpResponse>.Failure(Error("network.wait.single_use",ErrorKind.InvalidState));
                Started=true;Wait=wait;Start=Stopwatch.GetTimestamp();
                if(owner.Check(this)||State==HttpOperationState.Canceled){owner.operations.Remove(this);Ended.TrySetResult();return await Done.Task;}
                Driver=owner.Drive(this).Preserve();
                while(true){if(owner.Check(this))return await Done.Task;if(Done.Task.Status!=UniTaskStatus.Pending)return await Done.Task;await UniTask.Yield();}
            }
        }
        public UniTask<Result> StopAsync(CleanupContext cleanup)
        {Main();stopped=true;foreach(var op in operations.ToArray())Cancel(op,Error("network.owner.closed",ErrorKind.Canceled));return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            Main();var retryFailed=cleanupEntered;cleanupEntered=true;await StopAsync(cleanup);
            foreach(var op in operations.ToArray())await op.Ended.Task;
            if(retryFailed)foreach(var op in operations.ToArray())if(op.Exchange!=null&&op.ActualEnded&&await Release(op,cleanup))operations.Remove(op);
            return operations.Count==0?Result.Success():Result.Failure(Error("network.cleanup.retained",ErrorKind.CleanupFailed));
        }
        public DiagnosticCounters ReadDiagnostics(){Main();return new DiagnosticCounters{PendingOperations=operations.Count,FailedCleanup=operations.Count(o=>o.CleanupError.HasValue)};}
    }
}
