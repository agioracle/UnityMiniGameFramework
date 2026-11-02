using System;
using MiniFramework.Contracts;
using MiniFramework.Network;
using NUnit.Framework;
public sealed class HttpPolicyTests
{
    static HttpTransportResult Status(int code,params HttpHeader[] headers)=>HttpTransportResult.Received(new HttpResponse(code,Array.Empty<byte>(),headers));
    readonly DateTimeOffset now=new DateTimeOffset(2026,10,27,0,0,0,TimeSpan.Zero);
    [Test] public void RequestOwnsUpload(){var b=new byte[]{1};var r=new HttpRequestSpec(HttpVerb.Post,"https://example.test/",b);b[0]=9;r.CopyBody()[0]=8;Assert.AreEqual(1,r.CopyBody()[0]);}
    [Test] public void RequestOwnsHeaderArray(){var h=new[]{new HttpHeader("X-Test","one")};var r=new HttpRequestSpec(HttpVerb.Get,"https://example.test/",headers:h);h[0]=new HttpHeader("X-Test","two");Assert.AreEqual("one",r.Headers[0].Value);}
    [Test] public void RelativeUriRejected(){Assert.Throws<ArgumentException>(()=>new HttpRequestSpec(HttpVerb.Get,"relative"));}
    [Test] public void UnsupportedSchemeRejected(){Assert.Throws<ArgumentException>(()=>new HttpRequestSpec(HttpVerb.Get,"file:///tmp/x"));}
    [Test] public void EmbeddedCredentialsAndFragmentRejected(){foreach(var uri in new[]{"https://u:p@example.test/","https://example.test/#part"})Assert.Throws<ArgumentException>(()=>new HttpRequestSpec(HttpVerb.Get,uri));}
    [Test] public void ReadUploadRejected(){Assert.Throws<ArgumentException>(()=>new HttpRequestSpec(HttpVerb.Get,"https://example.test/",new byte[]{1}));}
    [Test] public void DuplicateHeadersCaseInsensitiveRejected(){Assert.Throws<ArgumentException>(()=>new HttpRequestSpec(HttpVerb.Get,"https://example.test/",headers:new[]{new HttpHeader("X-Test","1"),new HttpHeader("x-test","2")}));}
    [Test] public void FramingHeaderRejected(){Assert.Throws<ArgumentException>(()=>new HttpRequestSpec(HttpVerb.Post,"https://example.test/",headers:new[]{new HttpHeader("Content-Length","9")}));}
    [Test] public void HeaderControlCharactersRejected(){Assert.Throws<ArgumentException>(()=>new HttpHeader("X-Test","one\r\ntwo"));Assert.Throws<ArgumentException>(()=>new HttpHeader("Bad Name","x"));}
    [Test] public void ResponseReturnsCopies(){var b=new byte[]{1};var r=new HttpResponse(200,b);b[0]=9;r.CopyBody()[0]=8;Assert.AreEqual(1,r.CopyBody()[0]);}
    [Test] public void ResponseHeaderLookupIgnoresCase(){Assert.AreEqual("one",new HttpResponse(200,new byte[0],new[]{new HttpHeader("X-Test","one")}).Header("x-test"));}
    [Test] public void InvalidStatusRejected(){Assert.Throws<ArgumentException>(()=>new HttpResponse(0,new byte[0]));}
    [Test] public void DefaultLimitsBoundWork(){var l=new HttpClientLimits();Assert.AreEqual(16,l.MaxOperations);Assert.AreEqual(4,l.MaxConcurrent);Assert.AreEqual(1048576,l.MaxResponseBytes);Assert.AreEqual(262144,l.MaxUploadBytes);}
    [Test] public void InvalidLimitsRejected(){Assert.Throws<ArgumentOutOfRangeException>(()=>new HttpClientLimits(maxConcurrent:17));Assert.Throws<ArgumentOutOfRangeException>(()=>new HttpClientLimits(maxResponseBytes:0));}
    [Test] public void DefaultReadBackoffIsFinite(){var p=new HttpRetryPolicy();Assert.IsTrue(p.TryDelay(HttpVerb.Get,1,Status(503),now,out var a));Assert.AreEqual(100,a.TotalMilliseconds);Assert.IsTrue(p.TryDelay(HttpVerb.Get,2,Status(503),now,out var b));Assert.AreEqual(200,b.TotalMilliseconds);Assert.IsFalse(p.TryDelay(HttpVerb.Get,3,Status(503),now,out _));}
    [Test] public void EveryWriteVerbNeverRetries(){foreach(var v in new[]{HttpVerb.Post,HttpVerb.Put,HttpVerb.Patch,HttpVerb.Delete})Assert.IsFalse(new HttpRetryPolicy().TryDelay(v,1,Status(503),now,out _));}
    [Test] public void PermanentHttpFailuresNeverRetry(){foreach(var code in new[]{200,301,400,401,403,404,409,501})Assert.IsFalse(new HttpRetryPolicy().TryDelay(HttpVerb.Get,1,Status(code),now,out _));}
    [Test] public void SelectedTransientStatusesRetry(){foreach(var code in new[]{408,429,500,502,503,504})Assert.IsTrue(new HttpRetryPolicy().TryDelay(HttpVerb.Head,1,Status(code),now,out _));}
    [Test] public void RetryAfterSecondsIsHonored(){Assert.IsTrue(new HttpRetryPolicy().TryDelay(HttpVerb.Get,1,Status(429,new HttpHeader("Retry-After","1")),now,out var d));Assert.AreEqual(1,d.TotalSeconds);}
    [Test] public void RetryAfterDateIsHonored(){var date=now.AddSeconds(1).ToString("r");Assert.IsTrue(new HttpRetryPolicy().TryDelay(HttpVerb.Get,1,Status(503,new HttpHeader("Retry-After",date)),now,out var d));Assert.AreEqual(1,d.TotalSeconds);}
    [Test] public void TooLongRetryAfterDoesNotRetryEarly(){Assert.IsFalse(new HttpRetryPolicy().TryDelay(HttpVerb.Get,1,Status(429,new HttpHeader("Retry-After","60")),now,out _));}
    [Test] public void InvalidRetryAfterUsesBoundedBackoff(){Assert.IsTrue(new HttpRetryPolicy().TryDelay(HttpVerb.Get,1,Status(503,new HttpHeader("Retry-After","invalid")),now,out var d));Assert.AreEqual(100,d.TotalMilliseconds);}
    [Test] public void TransportRetryRequiresNetworkAndExplicitHint(){var e=new FrameworkError(ErrorKind.Network,"test.connection");Assert.IsTrue(new HttpRetryPolicy().TryDelay(HttpVerb.Get,1,HttpTransportResult.Failed(e,true),now,out _));Assert.IsFalse(new HttpRetryPolicy().TryDelay(HttpVerb.Get,1,HttpTransportResult.Failed(e),now,out _));Assert.IsFalse(new HttpRetryPolicy().TryDelay(HttpVerb.Get,1,HttpTransportResult.Failed(new FrameworkError(ErrorKind.Integrity,"test.body"),true),now,out _));}
    [Test] public void StatusErrorClassificationIsStable(){Assert.AreEqual(ErrorKind.PermissionDenied,HttpRetryPolicy.Classify(Status(401)).Kind);Assert.AreEqual(ErrorKind.Timeout,HttpRetryPolicy.Classify(Status(408)).Kind);Assert.AreEqual(ErrorKind.Unavailable,HttpRetryPolicy.Classify(Status(429)).Kind);Assert.AreEqual("network.http.503",HttpRetryPolicy.Classify(Status(503)).Code);}
    [Test] public void RetryPolicyRejectsUnboundedConfiguration(){Assert.Throws<ArgumentOutOfRangeException>(()=>new HttpRetryPolicy(99));Assert.Throws<ArgumentOutOfRangeException>(()=>new HttpRetryPolicy(initialDelay:TimeSpan.FromSeconds(-1)));}
    [Test] public void AggregateBufferedBudgetMustAdmitAtLeastOneResponse()
    {Assert.Throws<ArgumentOutOfRangeException>(()=>new HttpClientLimits(maxResponseBytes:1024,maxBufferedResponseBytesInFlight:1023));Assert.AreEqual(4096,new HttpClientLimits(maxResponseBytes:1024).MaxBufferedResponseBytesInFlight);}
}
