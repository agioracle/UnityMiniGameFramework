using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Host;
using MiniFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
public sealed class HostPauseTests
{
    [Test] public void PauseNotifiesImmediatelyWhileTicksAreSuppressed(){using(var host=new HostScheduler()){var tick=0;var pause=0;host.SubscribeTick((_,__)=>tick++);host.SubscribePause(p=>{Assert.IsTrue(p);pause++;});host.SetPaused(true);host.Pump(1,1);Assert.AreEqual(1,pause);Assert.AreEqual(0,tick);}}
    [Test] public void DuplicatePauseSignalDoesNotRepeatCallback(){using(var host=new HostScheduler()){var count=0;host.SubscribePause(_=>count++);host.SetPaused(true);host.SetPaused(true);host.SetPaused(false);host.SetPaused(false);Assert.AreEqual(2,count);}}
    [Test] public void PauseListenerCanRemoveAnotherDuringDispatch(){using(var host=new HostScheduler()){IDisposable second=null;var count=0;host.SubscribePause(_=>second.Dispose());second=host.SubscribePause(_=>count++);host.SetPaused(true);Assert.AreEqual(0,count);Assert.AreEqual(1,host.PauseListenerCount);}}
    [Test] public void PauseListenerExceptionIsObservedWithoutBlockingOthers(){using(var host=new HostScheduler()){var count=0;host.SubscribePause(_=>throw new InvalidOperationException("pause"));host.SubscribePause(_=>count++);host.SetPaused(true);Assert.AreEqual(1,count);Assert.AreEqual(1,host.ErrorCount);}}
    [Test] public void ReentrantResumeIsDeliveredAfterCurrentPauseBatch(){using(var host=new HostScheduler()){var states=new System.Collections.Generic.List<bool>();host.SubscribePause(p=>{states.Add(p);if(p)host.SetPaused(false);});host.SetPaused(true);CollectionAssert.AreEqual(new[]{true,false},states);Assert.IsFalse(host.IsPaused);}}
    [Test] public void HostDisposeClearsPauseListeners(){var host=new HostScheduler();host.SubscribePause(_=>{});Assert.AreEqual(1,host.PauseListenerCount);host.Dispose();Assert.AreEqual(0,host.PauseListenerCount);}
    [UnityTest] public IEnumerator RuntimeHostPauseHookForwardsThroughModule()=>UniTask.ToCoroutine(async()=>{var go=new GameObject("S305Host");var host=go.AddComponent<RuntimeHost>();var module=new RuntimeHostModule(host);var cleanup=new CleanupContext(default,System.Threading.Timeout.InfiniteTimeSpan);var context=new GameContext(new[]{new ModuleRegistration(module.Id,module.Dependencies,()=>module,typeof(IRuntimeScheduler),typeof(IApplicationLifecycle))},cleanup);try{Assert.IsTrue((await context.StartAsync(OperationContext.Unlimited())).IsSuccess);var count=0;using(module.SubscribePause(_=>count++)){host.SendMessage("OnApplicationPause",true);Assert.IsTrue(module.IsPaused);host.SendMessage("OnApplicationPause",false);Assert.IsFalse(module.IsPaused);Assert.AreEqual(2,count);}}finally{await context.CloseAsync(OperationContext.Unlimited());UnityEngine.Object.Destroy(go);}await UniTask.Yield();});
}
