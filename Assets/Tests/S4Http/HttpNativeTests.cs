using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.Network;
using MiniFramework.Network.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
public sealed class HttpNativeTests
{
    [Serializable] sealed class Info {public string baseUrl;}
    static OperationContext Wait=>new OperationContext(default,TimeSpan.FromSeconds(5));static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);
    readonly List<GameContext> contexts=new List<GameContext>();string baseUrl;
    [SetUp] public void Setup(){baseUrl=JsonUtility.FromJson<Info>(File.ReadAllText("Library/S403HttpServer.json")).baseUrl;Assert.IsTrue(baseUrl.StartsWith("http://127.0.0.1:",StringComparison.Ordinal));}
    async UniTask<HttpClientService> New(int maxResponse=1048576,int attempts=3)
    {
        var c=new HttpClientService(new UnityHttpTransport(),new HttpClientLimits(maxResponseBytes:maxResponse),new HttpRetryPolicy(attempts,TimeSpan.FromMilliseconds(10)));
        var context=new GameContext(new[]{new ModuleRegistration("network",Array.Empty<string>(),()=>c,typeof(IHttpClient))},Cleanup);contexts.Add(context);var started=await context.StartAsync(Wait);Assert.IsTrue(started.IsSuccess,started.IsSuccess?"":started.Error.Code);return c;
    }
    string PathFor(string kind)=>"/case/"+Guid.NewGuid().ToString("N")+"/"+kind;
    IHttpOperation Capture(HttpClientService client,string path,HttpVerb verb=HttpVerb.Get,byte[] bytes=null)
    {var result=client.Capture(new HttpRequestSpec(verb,baseUrl+path,bytes));Assert.IsTrue(result.IsSuccess);return result.Value;}
    async UniTask<int> Count(HttpClientService c,string path)
    {var r=await Capture(c,"/__count?path="+Uri.EscapeDataString(path)).WaitAsync(Wait);Assert.IsTrue(r.IsSuccess,r.IsSuccess?"":r.Error.Code);return int.Parse(Encoding.UTF8.GetString(r.Value.CopyBody()));}
    static async UniTask Drain(HttpClientService c)
    {for(var i=0;i<300&&c.PendingCount>0;i++)await UniTask.Yield();Assert.AreEqual(0,c.PendingCount);Assert.AreEqual(0,c.ActiveExchangeCount);Assert.AreEqual(0,c.RunningRequestCount);}
    [UnityTearDown] public IEnumerator Teardown()=>UniTask.ToCoroutine(async()=>{Time.timeScale=1;foreach(var c in contexts){var result=await c.CloseAsync(new OperationContext(default,TimeSpan.FromSeconds(5)));Assert.IsTrue(result.IsSuccess,result.IsSuccess?"":result.Error.Code);Assert.AreEqual(0,c.RetainedModuleCount);}contexts.Clear();});
    [UnityTest] public IEnumerator RealGetReturnsBodyHeadersAndReleases()=>UniTask.ToCoroutine(async()=>{var c=await New();var path=PathFor("ok");var op=Capture(c,path);var r=await op.WaitAsync(Wait);Assert.IsTrue(r.IsSuccess,r.IsSuccess?"":r.Error.Code);Assert.AreEqual("ok",Encoding.UTF8.GetString(r.Value.CopyBody()));Assert.AreEqual("1",r.Value.Header("x-case-attempt"));await Drain(c);Assert.AreEqual(1,await Count(c,path));});
    [UnityTest] public IEnumerator HeadDoesNotAllocateResourceLengthAsBody()=>UniTask.ToCoroutine(async()=>{var c=await New(1024);var op=Capture(c,PathFor("head-large"),HttpVerb.Head);var r=await op.WaitAsync(Wait);Assert.IsTrue(r.IsSuccess,r.IsSuccess?"":r.Error.Code);Assert.AreEqual(0,r.Value.BodyLength);await Drain(c);});
    [UnityTest] public IEnumerator Read503RetriesToSuccessExactlyThreeTimes()=>UniTask.ToCoroutine(async()=>{var c=await New();var path=PathFor("retry");var op=Capture(c,path);Assert.IsTrue((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(3,op.Attempts);Assert.AreEqual(3,await Count(c,path));await Drain(c);});
    [UnityTest] public IEnumerator Persistent503StopsAtConfiguredLimit()=>UniTask.ToCoroutine(async()=>{var c=await New();var path=PathFor("status-503");var op=Capture(c,path);Assert.AreEqual("network.http.503",(await op.WaitAsync(Wait)).Error.Code);Assert.AreEqual(3,await Count(c,path));await Drain(c);});
    [UnityTest] public IEnumerator EveryWriteVerbSendsOnlyOnceOn500()=>UniTask.ToCoroutine(async()=>{var c=await New();foreach(var verb in new[]{HttpVerb.Post,HttpVerb.Put,HttpVerb.Patch,HttpVerb.Delete}){var path=PathFor("status-500");var op=Capture(c,path,verb,new byte[]{1,2,3});Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1,op.Attempts);Assert.AreEqual(1,await Count(c,path));}await Drain(c);});
    [UnityTest] public IEnumerator PostRedirectNeverReplaysOrFollows()=>UniTask.ToCoroutine(async()=>{var c=await New();var path=PathFor("redirect");var op=Capture(c,path,HttpVerb.Post,new byte[]{1});Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1,op.Attempts);Assert.AreEqual(1,await Count(c,path));Assert.AreEqual(0,await Count(c,path.Substring(0,path.LastIndexOf('/'))+"/ok"));});
    [UnityTest] public IEnumerator GetRedirectIsExposedWithoutFollowing()=>UniTask.ToCoroutine(async()=>{var c=await New();var path=PathFor("redirect");var op=Capture(c,path);Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(1,await Count(c,path));Assert.AreEqual(0,await Count(c,path.Substring(0,path.LastIndexOf('/'))+"/ok"));});
    [UnityTest] public IEnumerator PermissionDeniedIsNotRetried()=>UniTask.ToCoroutine(async()=>{var c=await New();var path=PathFor("status-403");var op=Capture(c,path);Assert.AreEqual(ErrorKind.PermissionDenied,(await op.WaitAsync(Wait)).Error.Kind);Assert.AreEqual(1,await Count(c,path));});
    [UnityTest] public IEnumerator Client404DoesNotRetry()=>UniTask.ToCoroutine(async()=>{var c=await New();var path=PathFor("status-404");var op=Capture(c,path);Assert.AreEqual("network.http.404",(await op.WaitAsync(Wait)).Error.Code);Assert.AreEqual(1,await Count(c,path));});
    [UnityTest] public IEnumerator RetryAfter429EventuallySucceeds()=>UniTask.ToCoroutine(async()=>{var c=await New();var path=PathFor("retry-after");var op=Capture(c,path);Assert.IsTrue((await op.WaitAsync(Wait)).IsSuccess);Assert.AreEqual(2,await Count(c,path));});
    [UnityTest] public IEnumerator CancelRealSlowRequestAbortsAndDrains()=>UniTask.ToCoroutine(async()=>{var c=await New();var op=Capture(c,PathFor("slow"));using(var token=new CancellationTokenSource()){var task=op.WaitAsync(OperationContext.Unlimited(token.Token));await UniTask.Yield();token.Cancel();Assert.AreEqual(ErrorKind.Canceled,(await task).Error.Kind);await Drain(c);Assert.IsNull(op.Response);Assert.AreEqual(1,op.Attempts);}});
    [UnityTest] public IEnumerator RealTimeoutDisposesUnderlyingRequest()=>UniTask.ToCoroutine(async()=>{var c=await New();var op=Capture(c,PathFor("slow"));Assert.AreEqual(ErrorKind.Timeout,(await op.WaitAsync(new OperationContext(default,TimeSpan.FromMilliseconds(50)))).Error.Kind);await Drain(c);Assert.IsNull(op.Response);});
    [UnityTest] public IEnumerator PausedTimeScaleDoesNotStopDeadline()=>UniTask.ToCoroutine(async()=>{var c=await New();Time.timeScale=0;var op=Capture(c,PathFor("slow"));Assert.AreEqual(ErrorKind.Timeout,(await op.WaitAsync(new OperationContext(default,TimeSpan.FromMilliseconds(50)))).Error.Kind);await Drain(c);});
    [UnityTest] public IEnumerator ContentLengthOverLimitIsRejectedWithoutRetry()=>UniTask.ToCoroutine(async()=>{var c=await New(1024);var op=Capture(c,PathFor("oversize"));Assert.AreEqual("network.response.limit",(await op.WaitAsync(Wait)).Error.Code);Assert.AreEqual(1,op.Attempts);Assert.IsNull(op.Response);await Drain(c);});
    [UnityTest] public IEnumerator ChunkedBodyCannotBypassLimit()=>UniTask.ToCoroutine(async()=>{var c=await New(1024);var op=Capture(c,PathFor("chunked"));Assert.AreEqual("network.response.limit",(await op.WaitAsync(Wait)).Error.Code);Assert.AreEqual(1,op.Attempts);await Drain(c);});
    [UnityTest] public IEnumerator Truncated200IsNeverPublishedAsSuccess()=>UniTask.ToCoroutine(async()=>{var c=await New();var op=Capture(c,PathFor("truncated"));Assert.IsFalse((await op.WaitAsync(Wait)).IsSuccess);Assert.IsNull(op.Response);Assert.LessOrEqual(op.Attempts,3);await Drain(c);});
    [UnityTest] public IEnumerator ClosingContextDrainsRealPendingRequest()=>UniTask.ToCoroutine(async()=>{var c=await New();var op=Capture(c,PathFor("slow"));var task=op.WaitAsync(OperationContext.Unlimited());await UniTask.Yield();var close=await contexts[0].CloseAsync(Wait);Assert.IsTrue(close.IsSuccess,close.IsSuccess?"":close.Error.Code);Assert.AreEqual(ErrorKind.Canceled,(await task).Error.Kind);Assert.AreEqual(0,c.ActiveExchangeCount);});
    [UnityTest] public IEnumerator RepeatedRealRequestsReturnToZeroCounters()=>UniTask.ToCoroutine(async()=>{var c=await New();for(var i=0;i<10;i++)Assert.IsTrue((await Capture(c,PathFor("ok")).WaitAsync(Wait)).IsSuccess);await Drain(c);Assert.AreEqual(0,c.ReadDiagnostics().PendingOperations);});
}
