using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using NUnit.Framework;
using UnityEngine.TestTools;
namespace MiniFramework.Primitives.Tests
{
    public sealed class PrimitiveTests
    {
        private static CleanupContext Cleanup => new CleanupContext(CancellationToken.None, Timeout.InfiniteTimeSpan);
        private sealed class Node : IStateNode
        {
            internal Func<Result> OnEnter, OnExit;
            internal int Enters, Exits, Ticks;
            public Result Enter() { Enters++; return OnEnter == null ? Result.Success() : OnEnter(); }
            public Result Exit() { Exits++; return OnExit == null ? Result.Success() : OnExit(); }
            public Result Tick(float delta) { Ticks++; return Result.Success(); }
        }
        private sealed class Item { internal int Value; }
        [Test] public void RemovingEarlierListenerNeverRepeatsCurrent()
        {
            var e = new EventChannel<int>(); var trace = "";
            var a = e.Subscribe(_ => trace += "A"); e.Subscribe(_ => trace += "B");
            e.Subscribe(_ => { trace += "C"; a.Dispose(); }); e.Publish(0); e.Publish(0);
            Assert.AreEqual("ABCBC", trace);
        }
        [Test] public void RemoveBeforeTurnSkipsListener()
        {
            var e = new EventChannel<int>(); IDisposable b = null; var calls = 0;
            e.Subscribe(_ => b.Dispose()); b = e.Subscribe(_ => calls++); e.Publish(0); Assert.AreEqual(0, calls);
        }
        [Test] public void SelfRemovalIsIdempotent()
        {
            var e = new EventChannel<int>(); IDisposable s = null; var calls = 0;
            s = e.Subscribe(_ => { calls++; s.Dispose(); s.Dispose(); }); e.Publish(0); e.Publish(0);
            Assert.AreEqual(1, calls); Assert.AreEqual(0, e.ListenerCount);
        }
        [Test] public void AdditionExcludedFromCurrentDispatch()
        {
            var e = new EventChannel<int>(); var n = 0;
            e.Subscribe(_ => e.Subscribe(x => n++)); e.Publish(0); Assert.AreEqual(0,n);
            e.Publish(0); Assert.AreEqual(1,n);
        }
        [Test] public void NestedDispatchSeesNewListener()
        {
            var e = new EventChannel<int>(); var trace = "";
            e.Subscribe(x => { trace += "A" + x; if(x==0) { e.Subscribe(y => trace += "B" + y); e.Publish(1); } });
            e.Publish(0); Assert.AreEqual("A0A1B1",trace);
        }
        [Test] public void ListenerExceptionsContinueAndReturnFailure()
        {
            var e = new EventChannel<int>(); var n=0; e.Subscribe(_ => throw new Exception()); e.Subscribe(_ => n++);
            Assert.AreEqual(ErrorKind.Unexpected,e.Publish(0).Error.Kind); Assert.AreEqual(1,n);
        }
        [Test] public void RecursivePublishHasBound()
        {
            var e=new EventChannel<int>(); var n=0; Result last=default;
            e.Subscribe(_ => { n++; var r=e.Publish(0); if(!r.IsSuccess) last=r; }); e.Publish(0);
            Assert.AreEqual(32,n); Assert.AreEqual(ErrorKind.InvalidState,last.Error.Kind);
        }
        [Test] public void DisposeDuringDispatchSkipsRemaining()
        {
            var e=new EventChannel<int>(); e.Subscribe(_=>e.Dispose()); e.Subscribe(_=>Assert.Fail());
            Assert.IsTrue(e.Publish(0).IsSuccess); Assert.IsFalse(e.Publish(0).IsSuccess); Assert.AreEqual(0,e.ListenerCount);
        }
        [Test] public void EventInstancesAreIsolated()
        {
            var a=new EventChannel<int>(); var b=new EventChannel<int>(); var n=0;
            a.Subscribe(_=>n++); b.Publish(0); Assert.AreEqual(0,n);
        }
        [Test] public void StateViewIsReadOnlyAndEqualityFiltered()
        {
            var s=new StateValue<int>(1); var n=0; s.ReadOnly.Subscribe(_=>n++);
            s.Set(1); s.Set(2); Assert.AreEqual(1,n); Assert.AreEqual(2,s.ReadOnly.Value);
            Assert.AreNotSame(s,s.ReadOnly);
        }
        [Test] public void StateCommitsBeforeCallbackAndRejectsReentrantWrite()
        {
            var s=new StateValue<int>(0); Result nested=default;
            s.ReadOnly.Subscribe(x=> { Assert.AreEqual(x,s.ReadOnly.Value); nested=s.Set(9); }); s.Set(1);
            Assert.IsFalse(nested.IsSuccess); Assert.AreEqual(1,s.ReadOnly.Value);
        }
        [Test] public void StateListenerFailureDoesNotRollback()
        {
            var s=new StateValue<int>(0); s.ReadOnly.Subscribe(_=>throw new Exception());
            Assert.IsFalse(s.Set(1).IsSuccess); Assert.AreEqual(1,s.ReadOnly.Value);
        }
        [Test] public void StateComparerIsInstanceLocal()
        {
            var a=new StateValue<string>("a",StringComparer.OrdinalIgnoreCase); var b=new StateValue<string>("a");
            a.Set("A"); b.Set("A"); Assert.AreEqual("a",a.ReadOnly.Value); Assert.AreEqual("A",b.ReadOnly.Value);
        }
        [Test] public void ClosedStateRejectsWritesAndSubscriptions()
        {
            var s=new StateValue<int>(0); s.Dispose(); Assert.IsFalse(s.Set(1).IsSuccess);
            Assert.Throws<ObjectDisposedException>(()=>s.ReadOnly.Subscribe(_=>{}));
        }
        [Test] public void FsmTransitionOrderAndSameStateNoop()
        {
            var f=new StateMachine<int>(); var a=new Node(); var b=new Node(); f.Add(1,a); f.Add(2,b);
            f.Change(1); f.Change(1); f.Tick(1); f.Change(2);
            Assert.AreEqual(1,a.Enters); Assert.AreEqual(1,a.Exits); Assert.AreEqual(1,a.Ticks); Assert.AreEqual(2,f.Current);
            f.Dispose(); f.Dispose(); Assert.AreEqual(1,b.Exits);
        }
        [Test] public void FsmRejectsInvalidRegistrationAndUnknownTransition()
        {
            var f=new StateMachine<int>(); var a=new Node(); f.Add(1,a);
            Assert.IsFalse(f.Add(1,new Node()).IsSuccess); Assert.IsFalse(f.Add(2,a).IsSuccess);
            Assert.IsFalse(f.Change(9).IsSuccess); Assert.AreEqual(StateMachineStatus.Stopped,f.Status);
        }
        [Test] public void FsmRejectsReentrantTransition()
        {
            var f=new StateMachine<int>(); Result nested=default;
            f.Add(1,new Node{OnEnter=()=>{nested=f.Change(2);return Result.Success();}});f.Add(2,new Node());
            f.Change(1); Assert.IsFalse(nested.IsSuccess); Assert.AreEqual(1,f.Current);
        }
        [Test] public void FsmPartialEnterRetainsExitOwnership()
        {
            var f=new StateMachine<int>();var a=new Node{OnEnter=()=>throw new Exception()};f.Add(1,a);
            Assert.IsFalse(f.Change(1).IsSuccess); Assert.AreEqual(StateMachineStatus.Faulted,f.Status);
            Assert.IsFalse(f.Tick(0).IsSuccess); f.Dispose(); Assert.AreEqual(1,a.Exits);
        }
        [Test] public void FsmFailedExitRequiresExplicitRetry()
        {
            var f=new StateMachine<int>();var a=new Node{OnExit=()=>throw new Exception()};var b=new Node();f.Add(1,a);f.Add(2,b);f.Change(1);
            Assert.IsFalse(f.Change(2).IsSuccess); Assert.AreEqual(0,b.Enters); Assert.Throws<InvalidOperationException>(()=>f.Dispose());
            a.OnExit=null;f.Dispose();Assert.AreEqual(StateMachineStatus.Closed,f.Status);
        }
        [Test] public void FsmRejectsInvalidDelta()
        {
            var f=new StateMachine<int>();f.Add(0,new Node());f.Change(0);
            Assert.IsFalse(f.Tick(float.NaN).IsSuccess);Assert.IsFalse(f.Tick(-1).IsSuccess);Assert.IsTrue(f.Tick(0).IsSuccess);
        }
        [Test] public void PoolResetsAndReusesIdentity()
        {
            var p=new ObjectPool<Item>(()=>new Item(),x=>x.Value=0);var a=p.Rent().Value;var item=a.Value;item.Value=5;a.Return();
            var b=p.Rent().Value;Assert.AreSame(item,b.Value);Assert.AreEqual(0,b.Value.Value);b.Return();p.Dispose();
        }
        [Test] public void StaleLeaseCannotReturnNewRental()
        {
            var p=new ObjectPool<Item>(()=>new Item());var a=p.Rent().Value;var copy=a;a.Return();var b=p.Rent().Value;
            copy.Return();Assert.AreEqual(1,p.ActiveCount);Assert.Throws<ObjectDisposedException>(()=>{var unused=copy.Value;});b.Return();
        }
        [Test] public void PoolCapacityBoundsCache()
        {
            var p=new ObjectPool<Item>(()=>new Item(),capacity:1);var a=p.Rent().Value;var b=p.Rent().Value;a.Return();b.Return();
            Assert.AreEqual(1,p.CachedCount);Assert.AreEqual(0,p.ActiveCount);
        }
        [Test] public void PoolRejectsNullAndDuplicateFactoryIdentity()
        {
            Assert.IsFalse(new ObjectPool<Item>(()=>null).Rent().IsSuccess);var item=new Item();var p=new ObjectPool<Item>(()=>item);
            p.Rent();Assert.IsFalse(p.Rent().IsSuccess);Assert.AreEqual(1,p.ActiveCount);
        }
        [Test] public void PoolResetFailureRetainsLeaseForExplicitRetry()
        {
            var fail=true;var p=new ObjectPool<Item>(()=>new Item(),_=>{if(fail)throw new Exception();});var a=p.Rent().Value;
            Assert.AreEqual(ErrorKind.CleanupFailed,a.Return().Error.Kind);Assert.AreEqual(1,p.ActiveCount);
            Assert.Throws<InvalidOperationException>(()=>p.Dispose());fail=false;Assert.IsTrue(a.Return().IsSuccess);p.Dispose();Assert.IsFalse(p.Rent().IsSuccess);
        }
        [Test] public void PoolCallbackReentryRejected()
        {
            ObjectPool<Item> p=null;Result<ObjectPool<Item>.Lease> nested=default;
            p=new ObjectPool<Item>(()=>new Item(),_=>nested=p.Rent());var a=p.Rent().Value;a.Return();Assert.IsFalse(nested.IsSuccess);
        }
        [UnityTest] public IEnumerator ScopeOwnsEventsStateFsmAndRental() => UniTask.ToCoroutine(async ()=>
        {
            var scope=new Scope("primitives");var events=new EventChannel<int>();var state=new StateValue<int>(0);var f=new StateMachine<int>();var node=new Node();f.Add(0,node);f.Change(0);
            var p=new ObjectPool<Item>(()=>new Item());scope.TrackSubscription("pool",p);scope.TrackSubscription("fsm",f);
            Assert.IsTrue(events.Subscribe(scope,"event",_=>{}).IsSuccess);Assert.IsTrue(state.ReadOnly.Subscribe(scope,"state",_=>{}).IsSuccess);
            Assert.IsTrue(p.Rent(scope,"rental").IsSuccess);var report=await scope.CloseAsync(Cleanup);
            Assert.IsTrue(report.IsFullyDrained);Assert.AreEqual(0,events.ListenerCount);Assert.AreEqual(0,p.ActiveCount);Assert.AreEqual(1,node.Exits);
        });
        [UnityTest] public IEnumerator RejectedScopeRegistrationLeaksNothing() => UniTask.ToCoroutine(async ()=>
        {
            var scope=new Scope("reject");var events=new EventChannel<int>();var p=new ObjectPool<Item>(()=>new Item());
            events.Subscribe(scope,"same",_=>{});Assert.IsFalse(events.Subscribe(scope,"same",_=>{}).IsSuccess);Assert.AreEqual(1,events.ListenerCount);
            Assert.IsFalse(p.Rent(scope,"same").IsSuccess);Assert.AreEqual(0,p.ActiveCount);await scope.CloseAsync(Cleanup);
            Assert.IsFalse(events.Subscribe(scope,"late",_=>{}).IsSuccess);Assert.IsFalse(p.Rent(scope,"late").IsSuccess);
        });
        [UnityTest] public IEnumerator ScopeRetriesPoolResetFailure() => UniTask.ToCoroutine(async ()=>
        {
            var scope=new Scope("retry");var fail=true;var p=new ObjectPool<Item>(()=>new Item(),_=>{if(fail)throw new Exception();});
            scope.TrackSubscription("pool",p);p.Rent(scope,"rental");var first=await scope.CloseAsync(Cleanup);Assert.IsFalse(first.IsFullyDrained);
            fail=false;Assert.IsTrue((await scope.RetryFailedAsync(Cleanup)).IsFullyDrained);Assert.AreEqual(0,p.ActiveCount);
        });
        private sealed class PrimitiveModule : IModule
        {
            public string Id => "primitives";
            public IReadOnlyList<string> Dependencies => Array.Empty<string>();
            internal readonly EventChannel<int> Events=new EventChannel<int>();
            internal readonly StateValue<int> State=new StateValue<int>(0);
            internal readonly ObjectPool<Item> Pool=new ObjectPool<Item>(()=>new Item());
            internal readonly StateMachine<int> Fsm=new StateMachine<int>();
            internal bool Fail;
            internal int Received;
            public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
            {
                var scope=((IScopedModuleContext)context).Scope;
                scope.TrackSubscription("events",Events);scope.TrackSubscription("state",State);
                scope.TrackSubscription("pool",Pool);scope.TrackSubscription("fsm",Fsm);
                Events.Subscribe(scope,"listener",x=>Received+=x);
                State.ReadOnly.Subscribe(scope,"state-listener",x=>Received+=x);
                Pool.Rent(scope,"lease");Fsm.Add(0,new Node());Fsm.Change(0);
                return UniTask.FromResult(Fail?Result.Failure(new FrameworkError(ErrorKind.Provider,"test.init")):Result.Success());
            }
            public UniTask<Result> StopAsync(CleanupContext context)=>UniTask.FromResult(Result.Success());
            public UniTask<Result> DisposeAsync(CleanupContext context)=>UniTask.FromResult(Result.Success());
        }
        private static GameContext Context(PrimitiveModule module)=>new GameContext(new[]{new ModuleRegistration(module.Id,module.Dependencies,()=>module)},Cleanup);
        [UnityTest] public IEnumerator TwoContextsIsolateEventsAndState() => UniTask.ToCoroutine(async ()=>
        {
            var a=new PrimitiveModule();var b=new PrimitiveModule();var ca=Context(a);var cb=Context(b);
            await ca.StartAsync(OperationContext.Unlimited());await cb.StartAsync(OperationContext.Unlimited());
            a.Events.Publish(2);a.State.Set(3);Assert.AreEqual(5,a.Received);Assert.AreEqual(0,b.Received);Assert.AreEqual(0,b.State.ReadOnly.Value);
            await ca.CloseAsync(OperationContext.Unlimited());b.Events.Publish(1);Assert.AreEqual(1,b.Received);
            Assert.IsTrue((await cb.CloseAsync(OperationContext.Unlimited())).IsSuccess);
        });
        [UnityTest] public IEnumerator InitializationRollbackReclaimsAllPrimitives() => UniTask.ToCoroutine(async ()=>
        {
            var module=new PrimitiveModule{Fail=true};var context=Context(module);
            Assert.IsFalse((await context.StartAsync(OperationContext.Unlimited())).IsSuccess);
            await context.CloseAsync(OperationContext.Unlimited());Assert.AreEqual(0,module.Events.ListenerCount);
            Assert.AreEqual(0,module.Pool.ActiveCount);Assert.AreEqual(StateMachineStatus.Closed,module.Fsm.Status);
        });
        [UnityTest] public IEnumerator HundredContextCyclesReturnPrimitiveOwnershipToBaseline() => UniTask.ToCoroutine(async ()=>
        {
            for(var i=0;i<100;i++)
            {
                var module=new PrimitiveModule();var context=Context(module);
                Assert.IsTrue((await context.StartAsync(OperationContext.Unlimited())).IsSuccess);
                module.Events.Publish(1);module.State.Set(1);module.Fsm.Tick(0);
                Assert.IsTrue((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);
                Assert.AreEqual(0,module.Events.ListenerCount);Assert.AreEqual(0,module.Pool.ActiveCount);
                Assert.AreEqual(0,context.RetainedModuleCount);Assert.AreEqual(StateMachineStatus.Closed,module.Fsm.Status);
            }
        });
        [UnityTest] public IEnumerator FactoryClosingScopeCannotOrphanNewRental() => UniTask.ToCoroutine(async ()=>
        {
            var scope=new Scope("reentry");var p=new ObjectPool<Item>(()=>{scope.CloseAsync(default).Forget();return new Item();});
            Assert.IsFalse(p.Rent(scope,"lease").IsSuccess);Assert.AreEqual(1,p.ActiveCount);
            Assert.IsFalse((await scope.CloseAsync(Cleanup)).IsFullyDrained);
            Assert.IsTrue((await scope.RetryFailedAsync(Cleanup)).IsFullyDrained);Assert.AreEqual(0,p.ActiveCount);p.Dispose();
        });
        [Test] public void WarmPublishSetAndRentReturnAllocateNoManagedBytes()
        {
            var e=new EventChannel<int>();e.Subscribe(_=>{});var s=new StateValue<int>(0);s.ReadOnly.Subscribe(_=>{});var p=new ObjectPool<Item>(()=>new Item());
            for(var i=0;i<100;i++){e.Publish(i);s.Set(i);p.Rent().Value.Return();}
            var before=GC.GetAllocatedBytesForCurrentThread();
            for(var i=0;i<1000;i++){e.Publish(i);s.Set(i);p.Rent().Value.Return();}
            Assert.AreEqual(0,GC.GetAllocatedBytesForCurrentThread()-before);
        }
    }
}
