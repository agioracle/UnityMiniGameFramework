using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Host;
using MiniFramework.Runtime;
using UnityEngine;
namespace MiniFramework.Diagnostics.Tests
{
    public static class G1Scenario
    {
        private sealed class Module : IModule
        {
            public string Id {get;}
            public IReadOnlyList<string> Dependencies {get;}
            internal readonly EventChannel<int> Events=new EventChannel<int>();
            internal readonly StateValue<int> Value=new StateValue<int>(0);
            internal readonly ObjectPool<object> Pool=new ObjectPool<object>(()=>new object());
            internal readonly bool fail;
            public Module(int index,int failIndex){Id="work"+index;Dependencies=index==0?new[]{"runtime-host"}:new[]{"runtime-host","work"+(index-1)};fail=index==failIndex;}
            public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
            {
                var root=((IScopedModuleContext)context).Scope;
                Check(root.TrackSubscription("events",Events));Check(root.TrackSubscription("value",Value));Check(root.TrackSubscription("pool",Pool));
                var child=root.CreateChild("view",ScopeKind.View).Value;
                Check(Events.Subscribe(child,"event",_=>{}));Check(Value.ReadOnly.Subscribe(child,"value",_=>{}));
                if(!Pool.Rent(child,"lease").IsSuccess)throw new Exception("rent");
                if(!context.Services.TryGet<IRuntimeScheduler>(out var host))throw new Exception("host injection");
                Check(child.TrackSubscription("timer",host.Schedule(100,ClockDomain.Scaled,()=>{})));
                Check(child.TrackSubscription("tick",host.SubscribeTick((a,b)=>{})));
                Check(child.TrackSubscription("operation",host.Observe(OperationContext.Unlimited(),_=>{})));
                Check(child.RunTask("task",_=>UniTask.FromResult(Result.Success())));
                Events.Publish(1);Value.Set(1);
                return UniTask.FromResult(fail?Result.Failure(new FrameworkError(ErrorKind.Provider,"g1.injected")):Result.Success());
            }
            private static void Check(Result<ICleanupTicket> result){if(!result.IsSuccess)throw new Exception(result.Error.Code);}
            public UniTask<Result> StopAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
            public UniTask<Result> DisposeAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        }
        public static async UniTask RunAsync(int failIndex=-1)
        {
            var go=new GameObject("G1 acceptance host");GameContext context=null;
            var cleanup=new CleanupContext(CancellationToken.None,Timeout.InfiniteTimeSpan);
            try
            {
                var host=go.AddComponent<RuntimeHost>();var scheduler=host.Scheduler;
                var provider=new RuntimeHostModule(host);var registrations=new List<ModuleRegistration>{new ModuleRegistration(provider.Id,provider.Dependencies,()=>provider,typeof(IRuntimeScheduler))};
                var samples=new DiagnosticSession{Enabled=true};samples.Add("host",scheduler);
                for(var i=0;i<3;i++){var m=new Module(i,failIndex);registrations.Add(new ModuleRegistration(m.Id,m.Dependencies,()=>m));samples.Add(m.Id+"events",m.Events);samples.Add(m.Id+"state",m.Value);samples.Add(m.Id+"pool",m.Pool);}
                context=new GameContext(registrations,cleanup);samples.Add("context",context);
                var start=await context.StartAsync(OperationContext.Unlimited());
                if(start.IsSuccess!=(failIndex<0))throw new Exception("startup outcome");
                if(start.IsSuccess)
                {
                    var before=samples.Capture();if(before.ActiveScopes!=7||before.Listeners!=6||before.Timers!=3||before.PendingOperations!=3||before.ActiveRentals!=3)throw new Exception("live counters");
                    if(context.CaptureModules().Length!=4)throw new Exception("module snapshot");
                    await UniTask.NextFrame();
                }
                if(!(await context.CloseAsync(OperationContext.Unlimited())).IsSuccess)throw new Exception("close");
                var after=samples.Capture();
                if(after.ActiveScopes+after.Listeners+after.Timers+after.Ticks+after.PendingOperations+after.FailedCleanup+after.QueuedCallbacks+after.ActiveRentals+after.RetainedModules!=0)throw new Exception("ownership leak");
                if(context.TryGet<IRuntimeScheduler>(out _))throw new Exception("closed facade");
                samples.Dispose();
            }
            finally
            {
                if(context!=null)await context.CloseAsync(OperationContext.Unlimited());
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
