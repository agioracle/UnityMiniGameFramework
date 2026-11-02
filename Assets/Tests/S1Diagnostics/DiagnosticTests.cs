using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using NUnit.Framework;
using UnityEngine.TestTools;
namespace MiniFramework.Diagnostics.Tests
{
    public sealed class DiagnosticTests
    {
        private sealed class Source : IDiagnosticSource {public int Calls;public DiagnosticCounters ReadDiagnostics(){Calls++;return new DiagnosticCounters{Listeners=2};}}
        private static CleanupContext Cleanup=>new CleanupContext(CancellationToken.None,Timeout.InfiniteTimeSpan);
        [Test] public void DisabledByDefaultDoesNotReadSources(){var d=new DiagnosticSession();var s=new Source();d.Add("s",s);Assert.AreEqual(0,d.Capture().Listeners);Assert.AreEqual(0,s.Calls);}
        [Test] public void ExplicitSamplingAndRemove(){var d=new DiagnosticSession{Enabled=true};var s=new Source();d.Add("s",s);Assert.AreEqual(2,d.Capture().Listeners);Assert.AreEqual(1,s.Calls);d.Remove("s");Assert.AreEqual(0,d.Capture().Listeners);}
        [Test] public void RejectsDoubleCountingSameSource(){var d=new DiagnosticSession();var s=new Source();d.Add("s",s);Assert.Throws<ArgumentException>(()=>d.Add("second",s));Assert.Throws<ArgumentException>(()=>d.Add("s",new Source()));}
        [Test] public void SamplingDoesNotAllocateAfterWarmup(){var d=new DiagnosticSession{Enabled=true};d.Add("s",new Source());d.Capture();var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++)d.Capture();Assert.AreEqual(0,GC.GetAllocatedBytesForCurrentThread()-before);d.Enabled=false;before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++)d.Capture();Assert.AreEqual(0,GC.GetAllocatedBytesForCurrentThread()-before);}
        [UnityTest] public IEnumerator PendingTaskRemainsVisibleUntilActualCompletion()=>UniTask.ToCoroutine(async()=>{var s=new Scope("s");var source=new UniTaskCompletionSource<Result>();s.RunTask("work",_=>source.Task);Assert.AreEqual(1,s.ReadDiagnostics().PendingOperations);await s.CloseAsync(default);Assert.AreEqual(1,s.ReadDiagnostics().PendingOperations);source.TrySetResult(Result.Success());await s.CloseAsync(Cleanup);Assert.AreEqual(0,s.ReadDiagnostics().PendingOperations);Assert.AreEqual(0,s.ReadDiagnostics().ActiveScopes);});
        [UnityTest] public IEnumerator ChildCountsFollowIndependentClosure()=>UniTask.ToCoroutine(async()=>{var s=new Scope("s");var child=(Scope)s.CreateChild("child",ScopeKind.View).Value;Assert.AreEqual(2,s.ReadDiagnostics().ActiveScopes);await child.CloseAsync(Cleanup);Assert.AreEqual(1,s.ReadDiagnostics().ActiveScopes);await s.CloseAsync(Cleanup);Assert.AreEqual(0,s.ReadDiagnostics().ActiveScopes);});
        [UnityTest] public IEnumerator FullContextHundredCycles()=>UniTask.ToCoroutine(async()=>{for(var i=0;i<100;i++)await G1Scenario.RunAsync();});
        [UnityTest] public IEnumerator FailureAtFirstInitialization()=>UniTask.ToCoroutine(()=>G1Scenario.RunAsync(0));
        [UnityTest] public IEnumerator FailureAtMiddleInitialization()=>UniTask.ToCoroutine(()=>G1Scenario.RunAsync(1));
        [UnityTest] public IEnumerator FailureAtLastInitialization()=>UniTask.ToCoroutine(()=>G1Scenario.RunAsync(2));
    }
}
