using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Network;
using NUnit.Framework;
using UnityEngine.TestTools;
public sealed class HttpClientTests
{
    static OperationContext Wait=>OperationContext.Unlimited();static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);
    static FrameworkError Error(string code,ErrorKind kind=ErrorKind.Network)=>new FrameworkError(kind,code);
    static HttpTransportResult Status(int code)=>HttpTransportResult.Received(new HttpResponse(code,new byte[]{1}));
    sealed class Transport:IHttpTransport
    {
        internal readonly List<Exchange> Items=new List<Exchange>();internal readonly Queue<HttpTransportResult> Results=new Queue<HttpTransportResult>();
        internal bool Hold,FailCleanup,ThrowSend,ThrowOpen,FailAbort,ThrowCleanup;
        public IHttpExchange Open(HttpRequestSpec request,int maxResponseBytes)
        {if(ThrowOpen)throw new Exception();var x=new Exchange(this,Results.Count>0?Results.Dequeue():Status(200));Items.Add(x);return x;}
    }
    sealed class Exchange:IHttpExchange
    {
        readonly Transport owner;readonly HttpTransportResult result;readonly UniTaskCompletionSource<HttpTransportResult> done=new UniTaskCompletionSource<HttpTransportResult>();
        internal int Aborts,Cleanups,Sends;internal bool Ended;
        internal Exchange(Transport owner,HttpTransportResult result){this.owner=owner;this.result=result;}
        public UniTask<HttpTransportResult> SendAsync(){Sends++;if(owner.ThrowSend){Ended=true;throw new OperationCanceledException("unrelated");}if(owner.Hold)return done.Task;Ended=true;return UniTask.FromResult(result);}
        internal void Complete(){if(Ended)return;Ended=true;done.TrySetResult(result);}
        public Result Abort(){Aborts++;return owner.FailAbort?Result.Failure(Error("test.abort",ErrorKind.Provider)):Result.Success();}
        public UniTask<Result> CleanupAsync(CleanupContext cleanup){Cleanups++;if(owner.ThrowCleanup)throw new Exception();return UniTask.FromResult(owner.FailCleanup?Result.Failure(Error("test.cleanup",ErrorKind.CleanupFailed)):Result.Success());}
    }
    sealed class Env
    {
        internal readonly Transport Transport=new Transport();internal readonly HttpClientService Client;internal readonly GameContext Context;
        internal Env(int maxOps=16,int maxConcurrent=4,int maxResponse=1048576,TimeSpan? delay=null,string dependency=null)
        {Client=new HttpClientService(Transport,new HttpClientLimits(maxOps,maxConcurrent,maxResponseBytes:maxResponse),new HttpRetryPolicy(initialDelay:delay??TimeSpan.Zero),transportModuleId:dependency);Context=new GameContext(new[]{new ModuleRegistration("network",Client.Dependencies,()=>Client,typeof(IHttpClient))},Cleanup);}
        internal async UniTask Start(){Check(await Context.StartAsync(Wait));}
        internal IHttpOperation Capture(HttpVerb verb=HttpVerb.Get){var r=Client.Capture(new HttpRequestSpec(verb,"https://example.test/path?opaque=secret"));Assert.IsTrue(r.IsSuccess);return r.Value;}
        internal async UniTask Finish()
        {
            Transport.FailCleanup=Transport.ThrowCleanup=false;foreach(var x in Transport.Items)x.Complete();
            var closing=Context.CloseAsync(Wait);for(var i=0;i<5;i++){foreach(var x in Transport.Items.ToArray())x.Complete();await UniTask.Yield();}
            var result=await closing;if(!result.IsSuccess)result=await Context.RetryCleanupAsync(Cleanup,Wait);Check(result);Assert.AreEqual(0,Context.RetainedModuleCount);
        }
    }
    readonly List<Env> envs=new List<Env>();Env New(int maxOps=16,int concurrent=4,int response=1048576,TimeSpan? delay=null,string dependency=null){var e=new Env(maxOps,concurrent,response,delay,dependency);envs.Add(e);return e;}
    static void Check(Result result)=>Assert.IsTrue(result.IsSuccess,result.IsSuccess?"":result.Error.Code);
    static async UniTask Frames(int n=3){for(var i=0;i<n;i++)await UniTask.Yield();}
    [UnityTearDown] public IEnumerator Teardown()=>UniTask.ToCoroutine(async()=>{foreach(var e in envs)await e.Finish();envs.Clear();});
    [UnityTest] public IEnumerator ContextResolvesHttpContract()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();Assert.IsTrue(e.Context.TryGet<IHttpClient>(out var client));Assert.AreSame(e.Client,client);});
    [UnityTest] public IEnumerator ConstructionAndCaptureAreCold()=>UniTask.ToCoroutine(async()=>{var e=New();Assert.AreEqual(0,e.Transport.Items.Count);await e.Start();e.Capture();Assert.AreEqual(0,e.Transport.Items.Count);});
    [UnityTest] public IEnumerator NotReadyAndStoppedRejectCapture()=>UniTask.ToCoroutine(async()=>{var e=New();Assert.IsFalse(e.Client.Capture(new HttpRequestSpec(HttpVerb.Get,"https://example.test/")).IsSuccess);await e.Start();await e.Context.CloseAsync(Wait);Assert.IsFalse(e.Client.Capture(new HttpRequestSpec(HttpVerb.Get,"https://example.test/")).IsSuccess);});
    [UnityTest] public IEnumerator SingleWaitOnly()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var op=e.Capture();Assert.IsTrue((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual("network.wait.single_use",(await op.WaitAsync(Wait)).Error.Code);Assert.AreEqual(1,e.Transport.Items.Count);});
    [UnityTest] public IEnumerator PreCanceledDoesNotOpenTransport()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();using(var c=new CancellationTokenSource()){c.Cancel();Assert.AreEqual(ErrorKind.Canceled,(await e.Capture().WaitAsync(OperationContext.Unlimited(c.Token))).Error.Kind);}Assert.AreEqual(0,e.Transport.Items.Count);});
    [UnityTest] public IEnumerator ZeroTimeoutNeverSends()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();Assert.AreEqual(ErrorKind.Timeout,(await e.Capture().WaitAsync(default)).Error.Kind);Assert.AreEqual(0,e.Transport.Items.Count);});
    [UnityTest] public IEnumerator CanceledWinsOverZeroDeadline()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();using(var c=new CancellationTokenSource()){c.Cancel();Assert.AreEqual(ErrorKind.Canceled,(await e.Capture().WaitAsync(new OperationContext(c.Token,TimeSpan.Zero))).Error.Kind);}});
    [UnityTest] public IEnumerator CancelRetainsActualExchangeUntilTerminalCleanup()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Transport.Hold=true;using(var c=new CancellationTokenSource()){var op=e.Capture();var task=op.WaitAsync(OperationContext.Unlimited(c.Token));await Frames();c.Cancel();Assert.AreEqual(ErrorKind.Canceled,(await task).Error.Kind);Assert.AreEqual(1,e.Transport.Items[0].Aborts);Assert.AreEqual(1,e.Client.RunningRequestCount);Assert.AreEqual(0,e.Transport.Items[0].Cleanups);e.Transport.Items[0].Complete();await Frames();Assert.IsNull(op.Response);Assert.AreEqual(HttpOperationState.Canceled,op.State);Assert.AreEqual(0,e.Client.ActiveExchangeCount);}});
    [UnityTest] public IEnumerator TimeoutRetainsAndDiscardsLateResponse()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Transport.Hold=true;var op=e.Capture();var task=op.WaitAsync(new OperationContext(default,TimeSpan.FromMilliseconds(5)));await UniTask.Delay(20,ignoreTimeScale:true);Assert.AreEqual(ErrorKind.Timeout,(await task).Error.Kind);e.Transport.Items[0].Complete();await Frames();Assert.IsNull(op.Response);Assert.AreEqual(0,e.Client.PendingCount);});
    [UnityTest] public IEnumerator AbortFailureIsRecordedWithoutPrematureRelease()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Transport.Hold=true;e.Transport.FailAbort=true;using(var c=new CancellationTokenSource()){var op=e.Capture();var task=op.WaitAsync(OperationContext.Unlimited(c.Token));c.Cancel();await task;Assert.AreEqual("test.abort",op.AbortError.Value.Code);Assert.AreEqual(1,e.Client.ActiveExchangeCount);e.Transport.Items[0].Complete();await Frames();Assert.AreEqual(0,e.Client.ActiveExchangeCount);}});
    [UnityTest] public IEnumerator ReadRetriesOnlyAfterPreviousCleanup()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Transport.Results.Enqueue(Status(503));e.Transport.Results.Enqueue(Status(200));var op=e.Capture();Assert.IsTrue((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(2,op.Attempts);Assert.AreEqual(1,e.Transport.Items[0].Cleanups);Assert.AreEqual(0,e.Client.ActiveExchangeCount);});
    [UnityTest] public IEnumerator ReadAttemptsExhaustAtThree()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();for(var i=0;i<3;i++)e.Transport.Results.Enqueue(Status(503));var op=e.Capture();Assert.AreEqual("network.http.503",(await op.WaitAsync(Wait)).Error.Code);Assert.AreEqual(3,op.Attempts);Assert.AreEqual(503,op.Response.StatusCode);});
    [UnityTest] public IEnumerator WritesNeverRetryServerFailures()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();foreach(var verb in new[]{HttpVerb.Post,HttpVerb.Put,HttpVerb.Patch,HttpVerb.Delete}){e.Transport.Results.Enqueue(Status(500));var op=e.Capture(verb);Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1,op.Attempts);}});
    [UnityTest] public IEnumerator PermanentReadFailureIsNotRetried()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Transport.Results.Enqueue(Status(403));var op=e.Capture();Assert.AreEqual(ErrorKind.PermissionDenied,(await op.WaitAsync(Wait)).Error.Kind);Assert.AreEqual(1,op.Attempts);});
    [UnityTest] public IEnumerator RetryableConnectionFailuresAreBounded()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Transport.Results.Enqueue(HttpTransportResult.Failed(Error("test.connection"),true));var op=e.Capture();Assert.IsTrue((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(2,op.Attempts);});
    [UnityTest] public IEnumerator CancellationDuringBackoffPreventsNextSend()=>UniTask.ToCoroutine(async()=>{var e=New(delay:TimeSpan.FromMilliseconds(200));await e.Start();e.Transport.Results.Enqueue(Status(503));using(var c=new CancellationTokenSource()){var op=e.Capture();var task=op.WaitAsync(OperationContext.Unlimited(c.Token));await Frames();Assert.AreEqual(HttpOperationState.BackingOff,op.State);c.Cancel();await task;await Frames();Assert.AreEqual(1,e.Transport.Items.Count);}});
    [UnityTest] public IEnumerator DeadlineIncludesBackoff()=>UniTask.ToCoroutine(async()=>{var e=New(delay:TimeSpan.FromMilliseconds(100));await e.Start();e.Transport.Results.Enqueue(Status(503));var op=e.Capture();Assert.AreEqual(ErrorKind.Timeout,(await op.WaitAsync(new OperationContext(default,TimeSpan.FromMilliseconds(10)))).Error.Kind);Assert.AreEqual(1,op.Attempts);});
    [UnityTest] public IEnumerator ConcurrencySlotWaitIsCancelableWithoutOpening()=>UniTask.ToCoroutine(async()=>{var e=New(concurrent:1);await e.Start();e.Transport.Hold=true;var first=e.Capture().WaitAsync(Wait);using(var c=new CancellationTokenSource()){var op=e.Capture();var second=op.WaitAsync(OperationContext.Unlimited(c.Token));await Frames();Assert.AreEqual(HttpOperationState.Queued,op.State);Assert.AreEqual(1,e.Transport.Items.Count);c.Cancel();await second;e.Transport.Items[0].Complete();await first;Assert.AreEqual(1,e.Transport.Items.Count);}});
    [UnityTest] public IEnumerator SlotDeadlineIncludesQueueTime()=>UniTask.ToCoroutine(async()=>{var e=New(concurrent:1);await e.Start();e.Transport.Hold=true;var first=e.Capture().WaitAsync(Wait);var op=e.Capture();Assert.AreEqual(ErrorKind.Timeout,(await op.WaitAsync(new OperationContext(default,TimeSpan.FromMilliseconds(5)))).Error.Kind);Assert.AreEqual(0,op.Attempts);e.Transport.Items[0].Complete();await first;});
    [UnityTest] public IEnumerator FailedCleanupRetainedUntilExplicitContextRetry()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Transport.FailCleanup=true;var op=e.Capture();Assert.AreEqual(ErrorKind.CleanupFailed,(await op.WaitAsync(Wait)).Error.Kind);Assert.AreEqual(1,e.Client.ActiveExchangeCount);Assert.IsFalse((await e.Context.CloseAsync(Wait)).IsSuccess);Assert.AreEqual(1,e.Transport.Items[0].Cleanups);Assert.AreEqual(1,e.Context.RetainedModuleCount);e.Transport.FailCleanup=false;Check(await e.Context.RetryCleanupAsync(Cleanup,Wait));Assert.AreEqual(2,e.Transport.Items[0].Cleanups);Assert.AreEqual(0,e.Client.PendingCount);});
    [UnityTest] public IEnumerator CleanupFailurePreventsRetryAndPreservesOriginalHttpError()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Transport.Results.Enqueue(Status(503));e.Transport.FailCleanup=true;var op=e.Capture();Assert.AreEqual("network.http.503",(await op.WaitAsync(Wait)).Error.Code);Assert.IsTrue(op.CleanupError.HasValue);Assert.AreEqual(1,op.Attempts);});
    [UnityTest] public IEnumerator CleanupExceptionsAreObservedAndRetained()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Transport.ThrowCleanup=true;var op=e.Capture();Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual("network.cleanup.exception",op.CleanupError.Value.Code);Assert.AreEqual(1,e.Client.PendingCount);});
    [UnityTest] public IEnumerator CloseAbortsAndWaitsForActualSend()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Transport.Hold=true;var op=e.Capture();var task=op.WaitAsync(Wait);var close=e.Context.CloseAsync(Wait);await Frames();Assert.AreEqual(UniTaskStatus.Pending,close.Status);Assert.AreEqual(0,e.Transport.Items[0].Cleanups);e.Transport.Items[0].Complete();Check(await close);Assert.AreEqual(ErrorKind.Canceled,(await task).Error.Kind);Assert.IsNull(op.Response);});
    [UnityTest] public IEnumerator CloseCancelsUnwaitedColdOperation()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var op=e.Capture();Check(await e.Context.CloseAsync(Wait));Assert.AreEqual(HttpOperationState.Canceled,op.State);Assert.AreEqual(0,e.Transport.Items.Count);});
    [UnityTest] public IEnumerator CapacityIsBoundedAndColdCancelRecoversIt()=>UniTask.ToCoroutine(async()=>{var e=New(maxOps:1,concurrent:1);await e.Start();var first=e.Capture();Assert.AreEqual("network.capacity",e.Client.Capture(new HttpRequestSpec(HttpVerb.Get,"https://example.test/")).Error.Code);await first.WaitAsync(default);Assert.IsTrue(e.Client.Capture(new HttpRequestSpec(HttpVerb.Get,"https://example.test/")).IsSuccess);});
    [UnityTest] public IEnumerator OversizedTransportBodyCannotPublishOrRetry()=>UniTask.ToCoroutine(async()=>{var e=New(response:4);await e.Start();e.Transport.Results.Enqueue(HttpTransportResult.Received(new HttpResponse(200,new byte[5])));var op=e.Capture();Assert.AreEqual(ErrorKind.Integrity,(await op.WaitAsync(Wait)).Error.Kind);Assert.IsNull(op.Response);Assert.AreEqual(1,op.Attempts);});
    [UnityTest] public IEnumerator UnrelatedOperationCanceledExceptionIsProviderFailure()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Transport.ThrowSend=true;Assert.AreEqual(ErrorKind.Provider,(await e.Capture().WaitAsync(Wait)).Error.Kind);Assert.AreEqual(0,e.Client.ActiveExchangeCount);});
    [UnityTest] public IEnumerator FactoryFailureDoesNotLeakSlot()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Transport.ThrowOpen=true;Assert.IsFalse((await e.Capture().WaitAsync(Wait)).IsSuccess);Assert.AreEqual(0,e.Client.ActiveExchangeCount);Assert.AreEqual(0,e.Client.PendingCount);});
    [UnityTest] public IEnumerator MissingTransportModuleFailsBeforeOpen()=>UniTask.ToCoroutine(async()=>{var e=New(dependency:"missing");Assert.IsFalse((await e.Context.StartAsync(Wait)).IsSuccess);Assert.AreEqual(0,e.Transport.Items.Count);});
    [UnityTest] public IEnumerator DiagnosticsAndErrorsDoNotContainRequestSecrets()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Transport.Results.Enqueue(Status(403));var op=e.Capture();var r=await op.WaitAsync(Wait);Assert.IsFalse(r.Error.Code.Contains("secret"));Assert.IsFalse(r.Error.Message.Contains("secret"));Assert.AreEqual(0,e.Client.ReadDiagnostics().PendingOperations);});
}
