using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Host;
using MiniFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
namespace MiniFramework.Host.Tests
{
    public sealed class HostTests
    {
        private sealed class Clock : IMonotonicClock { public double Now; public double Seconds=>Now; }
        private static CleanupContext Cleanup=>new CleanupContext(CancellationToken.None,Timeout.InfiniteTimeSpan);
        [Test] public void ScaledAndUnscaledClocksAreIndependent()
        {var h=new HostScheduler(new Clock());h.Pump(0,2);Assert.AreEqual(0,h.ScaledTime);Assert.AreEqual(2,h.UnscaledTime);h.Pump(1,2);Assert.AreEqual(1,h.ScaledTime);}
        [Test] public void TimerDomainsRespectTimescale()
        {var h=new HostScheduler(new Clock());var a=0;var b=0;h.Schedule(1,ClockDomain.Scaled,()=>a++);h.Schedule(1,ClockDomain.Unscaled,()=>b++);h.Pump(0,1);Assert.AreEqual(0,a);Assert.AreEqual(1,b);h.Pump(1,1);Assert.AreEqual(1,a);Assert.AreEqual(0,h.TimerCount);}
        [Test] public void PauseFreezesGameClocksAndDefersDispatch()
        {var c=new Clock();var h=new HostScheduler(c);var calls=0;h.Schedule(1,ClockDomain.Realtime,()=>calls++);h.Post(()=>calls++,()=>{});h.SetPaused(true);c.Now=10;h.Pump(10,10);Assert.AreEqual(0,calls);Assert.AreEqual(0,h.UnscaledTime);h.SetPaused(false);h.Pump(0,0);Assert.AreEqual(2,calls);}
        [Test] public void RepeatTimerSkipsMissedIntervals()
        {var c=new Clock();var h=new HostScheduler(c);var calls=0;h.Schedule(1,ClockDomain.Realtime,()=>calls++,1);c.Now=100;h.Pump(0,0);h.Pump(0,0);Assert.AreEqual(1,calls);c.Now=101;h.Pump(0,0);Assert.AreEqual(2,calls);}
        [Test] public void TimerRemovalBeforeTurnSkipsCallback()
        {var h=new HostScheduler(new Clock());IDisposable b=null;h.Schedule(0,ClockDomain.Scaled,()=>b.Dispose());b=h.Schedule(0,ClockDomain.Scaled,()=>Assert.Fail());Assert.IsTrue(h.Pump(0,0).IsSuccess);Assert.AreEqual(0,h.TimerCount);}
        [Test] public void AddedTimerRunsNextPump()
        {var h=new HostScheduler(new Clock());var n=0;h.Schedule(0,ClockDomain.Scaled,()=>h.Schedule(0,ClockDomain.Scaled,()=>n++));h.Pump(0,0);Assert.AreEqual(0,n);h.Pump(0,0);Assert.AreEqual(1,n);}
        [Test] public void TickSelfRemovalAndAdditionAreDeterministic()
        {var h=new HostScheduler(new Clock());IDisposable a=null;var n=0;a=h.SubscribeTick((x,y)=>{a.Dispose();h.SubscribeTick((u,v)=>n++);});h.Pump(1,1);Assert.AreEqual(0,n);h.Pump(1,1);Assert.AreEqual(1,n);}
        [Test] public void EmptySchedulerHasNoTickSubscribers()
        {var h=new HostScheduler(new Clock());h.Pump(1,1);Assert.AreEqual(0,h.TickCount);Assert.AreEqual(0,h.TimerCount);}
        [Test] public void CallbackExceptionsContinueOtherCallbacks()
        {var h=new HostScheduler(new Clock());var n=0;h.Schedule(0,ClockDomain.Scaled,()=>throw new Exception());h.SubscribeTick((x,y)=>n++);Assert.IsFalse(h.Pump(0,0).IsSuccess);Assert.AreEqual(1,n);Assert.AreEqual(1,h.ErrorCount);}
        [Test] public void ReentrantPumpRejected()
        {var h=new HostScheduler(new Clock());Result r=default;h.SubscribeTick((x,y)=>r=h.Pump(0,0));h.Pump(0,0);Assert.IsFalse(r.IsSuccess);}
        [Test] public void ZeroDeadlineExpiresImmediately()
        {var h=new HostScheduler(new Clock());ErrorKind kind=ErrorKind.None;var op=h.Observe(default,e=>kind=e.Kind);Assert.AreEqual(ErrorKind.Timeout,kind);Assert.IsFalse(op.IsPending);Assert.IsFalse(op.TryComplete(()=>Assert.Fail()).IsSuccess);}
        [Test] public void CancellationWinsOverDeadline()
        {var cts=new CancellationTokenSource();cts.Cancel();var h=new HostScheduler(new Clock());ErrorKind kind=ErrorKind.None;h.Observe(new OperationContext(cts.Token,TimeSpan.Zero),e=>kind=e.Kind);Assert.AreEqual(ErrorKind.Canceled,kind);cts.Dispose();}
        [Test] public void ResumeExpiresBeforeQueuedSuccess()
        {var c=new Clock();var h=new HostScheduler(c);var trace="";var op=h.Observe(new OperationContext(default,TimeSpan.FromSeconds(1)),e=>trace+="timeout");h.Post(()=>{trace+="queue";Assert.IsFalse(op.TryComplete(()=>trace+="success").IsSuccess);},()=>{});h.SetPaused(true);c.Now=2;h.Pump(0,0);Assert.AreEqual("",trace);h.SetPaused(false);h.Pump(0,0);Assert.AreEqual("timeoutqueue",trace);Assert.AreEqual(0,h.ErrorCount);}
        [Test] public void DeadlineCheckedEvenWithoutPump()
        {var c=new Clock();var h=new HostScheduler(c);var op=h.Observe(new OperationContext(default,TimeSpan.FromSeconds(1)),_=>{});c.Now=1;var committed=false;Assert.IsFalse(op.TryComplete(()=>committed=true).IsSuccess);Assert.IsFalse(committed);}
        [Test] public void SuccessfulOperationCommitsExactlyOnce()
        {var h=new HostScheduler(new Clock());var n=0;var op=h.Observe(OperationContext.Unlimited(),_=>Assert.Fail());Assert.IsTrue(op.TryComplete(()=>n++).IsSuccess);Assert.IsFalse(op.TryComplete(()=>n++).IsSuccess);h.Dispose();Assert.AreEqual(1,n);Assert.AreEqual(0,h.ErrorCount);}
        [Test] public void CancellationInsideFirstQueuedCallbackPrecedesSecond()
        {var h=new HostScheduler(new Clock());var cts=new CancellationTokenSource();var trace="";h.Observe(OperationContext.Unlimited(cts.Token),_=>trace+="cancel");h.Post(()=>cts.Cancel(),()=>{});h.Post(()=>trace+="next",()=>{});h.Pump(0,0);Assert.AreEqual("cancelnext",trace);cts.Dispose();}
        [Test] public void QueueAdditionsWaitForNextPump()
        {var h=new HostScheduler(new Clock());var n=0;h.Post(()=>h.Post(()=>n++,()=>{}),()=>{});h.Pump(0,0);Assert.AreEqual(0,n);h.Pump(0,0);Assert.AreEqual(1,n);}
        [Test] public void BoundedQueueRejectsWithoutTakingOwnership()
        {var h=new HostScheduler(new Clock(),1);var discarded=0;Assert.IsTrue(h.Post(()=>{},()=>discarded++));Assert.IsFalse(h.Post(()=>{},()=>Assert.Fail()));h.Dispose();Assert.AreEqual(1,discarded);Assert.IsFalse(h.Post(()=>{},()=>Assert.Fail()));}
        [Test] public void WorkerPostsDispatchOnlyOnOwnerThread()
        {var h=new HostScheduler(new Clock());var main=Thread.CurrentThread.ManagedThreadId;var actual=0;Exception failure=null;var worker=new Thread(()=>{try{h.Post(()=>actual=Thread.CurrentThread.ManagedThreadId,()=>{});}catch(Exception e){failure=e;}});worker.Start();worker.Join();Assert.IsNull(failure);Assert.AreEqual(0,actual);h.Pump(0,0);Assert.AreEqual(main,actual);}
        [Test] public void WorkerCannotScheduleOrPump()
        {var h=new HostScheduler(new Clock());Exception failure=null;var worker=new Thread(()=>{try{h.Pump(0,0);}catch(Exception e){failure=e;}});worker.Start();worker.Join();Assert.IsInstanceOf<InvalidOperationException>(failure);}
        [Test] public void DisposeDiscardsQueueAndCancelsOperationsOnce()
        {var h=new HostScheduler(new Clock());var n=0;h.Observe(OperationContext.Unlimited(),_=>n++);h.Post(()=>Assert.Fail(),()=>n++);h.Dispose();h.Dispose();Assert.AreEqual(2,n);Assert.AreEqual(0,h.ErrorCount);Assert.AreEqual(0,h.PendingOperationCount);}
        [Test] public void DisposeDuringDispatchSkipsRemainingWork()
        {var h=new HostScheduler(new Clock());var n=0;h.Post(()=>h.Dispose(),()=>{});h.Post(()=>Assert.Fail(),()=>n++);h.SubscribeTick((x,y)=>Assert.Fail());Assert.IsTrue(h.Pump(0,0).IsSuccess);Assert.AreEqual(1,n);Assert.AreEqual(0,h.TickCount);}
        [Test] public void FailedDiscardRetainsOwnershipForExplicitRetry()
        {var h=new HostScheduler(new Clock());var fail=true;var n=0;h.Post(()=>{},()=>{n++;if(fail)throw new Exception();});Assert.Throws<InvalidOperationException>(()=>h.Dispose());Assert.AreEqual(1,h.FailedDiscardCount);fail=false;h.Dispose();h.Dispose();Assert.AreEqual(2,n);Assert.AreEqual(0,h.FailedDiscardCount);}
        [Test] public void CancellationCallbackCanDisposeOtherOperation()
        {var h=new HostScheduler(new Clock());IHostOperation b=null;h.Observe(OperationContext.Unlimited(),_=>b.Dispose());b=h.Observe(OperationContext.Unlimited(),_=>{});h.Dispose();Assert.AreEqual(0,h.PendingOperationCount);}
        [Test] public void InvalidClockAndDeltaRejected()
        {var c=new Clock();var h=new HostScheduler(c);Assert.Throws<ArgumentOutOfRangeException>(()=>h.Pump(float.NaN,0));Assert.Throws<ArgumentOutOfRangeException>(()=>h.Schedule(-1,ClockDomain.Scaled,()=>{}));c.Now=-1;Assert.Throws<InvalidOperationException>(()=>{var t=h.Realtime;});}
        [Test] public void StableTickAndTimerPumpAllocatesZeroBytes()
        {var h=new HostScheduler(new Clock());h.SubscribeTick((a,b)=>{});h.Schedule(0,ClockDomain.Unscaled,()=>{},1);for(var i=0;i<100;i++)h.Pump(1,1);var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++)h.Pump(1,1);Assert.AreEqual(0,GC.GetAllocatedBytesForCurrentThread()-before);}
        [UnityTest] public IEnumerator ScopeClosesTimersTicksAndOperations()=>UniTask.ToCoroutine(async()=>
        {var h=new HostScheduler(new Clock());var s=new Scope("host");s.TrackSubscription("timer",h.Schedule(1,ClockDomain.Scaled,()=>Assert.Fail()));s.TrackSubscription("tick",h.SubscribeTick((x,y)=>Assert.Fail()));s.TrackSubscription("operation",h.Observe(OperationContext.Unlimited(),_=>{}));Assert.IsTrue((await s.CloseAsync(Cleanup)).IsFullyDrained);Assert.IsTrue(h.Pump(2,2).IsSuccess);Assert.AreEqual(0,h.TimerCount);Assert.AreEqual(0,h.TickCount);Assert.AreEqual(0,h.PendingOperationCount);});
        [UnityTest] public IEnumerator UnityHostTicksAndStopDoesNotRestart()
        {var go=new GameObject("host-test");try{var host=go.AddComponent<RuntimeHost>();var first=host.StartHost();Assert.AreSame(first,host.StartHost());var n=0;first.SubscribeTick((x,y)=>n++);yield return null;yield return null;Assert.Greater(n,0);host.StopHost();yield return null;Assert.IsFalse(host.IsRunning);Assert.AreNotSame(first,host.StartHost());}finally{UnityEngine.Object.DestroyImmediate(go);}}
        [UnityTest] public IEnumerator UnityPauseResumeUsesDeadlineBeforeQueue()
        {var go=new GameObject("host-pause");try{var host=go.AddComponent<RuntimeHost>();var s=host.Scheduler;var trace="";var op=s.Observe(new OperationContext(default,TimeSpan.FromMilliseconds(10)),_=>trace+="expired");go.SendMessage("OnApplicationPause",true);s.Post(()=>{trace+="queued";Assert.IsFalse(op.TryComplete(()=>Assert.Fail()).IsSuccess);},()=>{});yield return new WaitForSecondsRealtime(.03f);Assert.AreEqual("",trace);go.SendMessage("OnApplicationPause",false);yield return null;yield return null;Assert.AreEqual("expiredqueued",trace);Assert.AreEqual(0,s.ErrorCount);}finally{UnityEngine.Object.DestroyImmediate(go);}}
        [UnityTest] public IEnumerator SessionResetReplacesOldInstanceState()
        {var go=new GameObject("host-session");try{var host=go.AddComponent<RuntimeHost>();for(var i=0;i<10;i++){var old=host.Scheduler;old.SubscribeTick((x,y)=>{});typeof(RuntimeHost).GetMethod("ResetSession",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);Assert.AreNotSame(old,host.StartHost());Assert.AreEqual(0,old.TickCount);Assert.AreEqual(0,host.Scheduler.TickCount);}yield return null;}finally{UnityEngine.Object.DestroyImmediate(go);}}
        [UnityTest] public IEnumerator SessionResetClearsPausedState()
        {var go=new GameObject("host-reset-pause");try{var host=go.AddComponent<RuntimeHost>();go.SendMessage("OnApplicationPause",true);Assert.IsTrue(host.Scheduler.IsPaused);typeof(RuntimeHost).GetMethod("ResetSession",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);Assert.IsFalse(host.StartHost().IsPaused);yield return null;}finally{UnityEngine.Object.DestroyImmediate(go);}}
        [UnityTest] public IEnumerator ScopeRetainsFailedDiscardForRetry()=>UniTask.ToCoroutine(async()=>
        {var h=new HostScheduler(new Clock());var scope=new Scope("discard");var fail=true;h.Post(()=>{},()=>{if(fail)throw new Exception();});scope.TrackSubscription("host",h);Assert.IsFalse((await scope.CloseAsync(Cleanup)).IsFullyDrained);Assert.AreEqual(1,h.FailedDiscardCount);fail=false;Assert.IsTrue((await scope.RetryFailedAsync(Cleanup)).IsFullyDrained);Assert.AreEqual(0,h.FailedDiscardCount);});
        private sealed class Consumer : IModule
        {
            public string Id=>"consumer";public IReadOnlyList<string> Dependencies=>new[]{"runtime-host"};
            internal IRuntimeScheduler Scheduler;internal int Ticks;
            public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
            {Assert.IsTrue(context.Services.TryGet<IRuntimeScheduler>(out var scheduler));Scheduler=scheduler;var s=((IScopedModuleContext)context).Scope;s.TrackSubscription("tick",scheduler.SubscribeTick((x,y)=>Ticks++));s.TrackSubscription("timer",scheduler.Schedule(100,ClockDomain.Scaled,()=>Assert.Fail()));return UniTask.FromResult(Result.Success());}
            public UniTask<Result> StopAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
            public UniTask<Result> DisposeAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        }
        [UnityTest] public IEnumerator ContextInjectsHostAndClosesAfterConsumer()=>UniTask.ToCoroutine(async()=>
        {var go=new GameObject("context-host");try{var host=go.AddComponent<RuntimeHost>();var provider=new RuntimeHostModule(host);var consumer=new Consumer();var c=new GameContext(new[]{new ModuleRegistration(provider.Id,provider.Dependencies,()=>provider,typeof(IRuntimeScheduler)),new ModuleRegistration(consumer.Id,consumer.Dependencies,()=>consumer)},Cleanup);Assert.IsTrue((await c.StartAsync(OperationContext.Unlimited())).IsSuccess);await UniTask.NextFrame();await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);Assert.Greater(consumer.Ticks,0);var scheduler=host.Scheduler;Assert.IsTrue((await c.CloseAsync(OperationContext.Unlimited())).IsSuccess);Assert.IsFalse(host.IsRunning);Assert.AreEqual(0,scheduler.TimerCount);Assert.AreEqual(0,scheduler.TickCount);Assert.AreEqual(0,scheduler.ErrorCount);}finally{UnityEngine.Object.DestroyImmediate(go);}});
        [UnityTest] public IEnumerator HundredHostScopeCyclesReturnToBaseline()=>UniTask.ToCoroutine(async()=>
        {for(var i=0;i<100;i++){var h=new HostScheduler(new Clock());var scope=new Scope("cycle");scope.TrackSubscription("host",h);scope.TrackSubscription("tick",h.SubscribeTick((x,y)=>{}));scope.TrackSubscription("timer",h.Schedule(1,ClockDomain.Scaled,()=>{}));scope.TrackSubscription("op",h.Observe(OperationContext.Unlimited(),_=>{}));h.Post(()=>{},()=>{});Assert.IsTrue((await scope.CloseAsync(Cleanup)).IsFullyDrained);Assert.AreEqual(0,h.TickCount);Assert.AreEqual(0,h.TimerCount);Assert.AreEqual(0,h.PendingOperationCount);Assert.AreEqual(0,h.QueuedCount);}});
    }
}
