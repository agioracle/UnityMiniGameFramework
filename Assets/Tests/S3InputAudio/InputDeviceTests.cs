using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Game.Access;
using MiniFramework.Contracts;
using MiniFramework.Host;
using MiniFramework.Input;
using MiniFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
public sealed class InputDeviceTests
{
    static CleanupContext Cleanup=>new CleanupContext(default,System.Threading.Timeout.InfiniteTimeSpan);
    sealed class Source:IPointerSource
    {
        internal readonly Queue<PointerSample[]> Frames=new Queue<PointerSample[]>();internal bool Supported=true,InvalidFrame,Throw,RepeatMove;
        internal DeviceMetrics Metrics=new DeviceMetrics(1000,500,new ScreenArea(50,20,900,460));
        public bool IsSupported=>Supported;
        public Result<PointerFrame> Capture(PointerSample[] destination)
        {if(Throw)throw new InvalidOperationException("capture");if(InvalidFrame)return Result<PointerFrame>.Success(default);if(RepeatMove){destination[0]=new PointerSample(1,PointerPhase.Moved,new ScreenPoint(100,200));return Result<PointerFrame>.Success(new PointerFrame(Metrics,1));}var samples=Frames.Count==0?Array.Empty<PointerSample>():Frames.Dequeue();Array.Copy(samples,destination,samples.Length);return Result<PointerFrame>.Success(new PointerFrame(Metrics,samples.Length));}
        public void Reset(){Frames.Clear();}
        internal void Send(PointerPhase phase,int id=1,bool ui=false)=>Frames.Enqueue(new[]{new PointerSample(id,phase,new ScreenPoint(100,200),ui)});
    }
    sealed class HostModule:IModule
    {
        internal readonly HostScheduler Host=new HostScheduler();public string Id=>"runtime-host";public IReadOnlyList<string> Dependencies=>Array.Empty<string>();
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)=>UniTask.FromResult(Result.Success());
        public UniTask<Result> StopAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        public UniTask<Result> DisposeAsync(CleanupContext cleanup){Host.Dispose();return UniTask.FromResult(Result.Success());}
    }
    Source source;HostModule host;InputDeviceService input;GameContext context;readonly List<Scope> owners=new List<Scope>();readonly List<GameObject> objects=new List<GameObject>();
    Scope Owner(){var scope=new Scope(Guid.NewGuid().ToString("N"),ScopeKind.View);owners.Add(scope);return scope;}
    async UniTask Start(bool supported=true)
    {
        source=new Source{Supported=supported};host=new HostModule();input=new InputDeviceService(source,host.Host);
        context=new GameContext(new[]{new ModuleRegistration(input.Id,input.Dependencies,()=>input,typeof(IInputDeviceService)),new ModuleRegistration(host.Id,host.Dependencies,()=>host)},Cleanup);
        var result=await context.StartAsync(OperationContext.Unlimited());Assert.AreEqual(supported,result.IsSuccess);
    }
    void Pump(){host.Host.Pump(0.016f,0.016f);}
    [UnityTearDown] public IEnumerator TearDown()=>UniTask.ToCoroutine(async()=>{foreach(var owner in owners)Assert.IsTrue((await owner.CloseAsync(Cleanup)).IsFullyDrained);owners.Clear();if(context!=null)Assert.IsTrue((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);context=null;foreach(var go in objects)if(go)UnityEngine.Object.DestroyImmediate(go);objects.Clear();});
    [UnityTest] public IEnumerator FacadeResolvesInputOnlyAfterContextReady()=>UniTask.ToCoroutine(async()=>{await Start();Assert.IsTrue(new GameServices(context).InputDevice().IsSuccess);await context.CloseAsync(OperationContext.Unlimited());Assert.IsFalse(new GameServices(context).InputDevice().IsSuccess);});
    [UnityTest] public IEnumerator PointerBeginMoveEndDeliveredOnce()=>UniTask.ToCoroutine(async()=>{await Start();var phases=new List<PointerPhase>();input.Subscribe(Owner(),p=>phases.Add(p.Phase));source.Send(PointerPhase.Began);Pump();source.Send(PointerPhase.Moved);Pump();source.Send(PointerPhase.Ended);Pump();CollectionAssert.AreEqual(new[]{PointerPhase.Began,PointerPhase.Moved,PointerPhase.Ended},phases);});
    [UnityTest] public IEnumerator UiCapturePersistsAfterDraggingOutside()=>UniTask.ToCoroutine(async()=>{await Start();var game=0;var ui=0;input.Subscribe(Owner(),_=>game++);input.Subscribe(Owner(),_=>ui++,true);source.Send(PointerPhase.Began,ui:true);Pump();source.Send(PointerPhase.Moved);Pump();source.Send(PointerPhase.Ended);Pump();Assert.AreEqual(0,game);Assert.AreEqual(3,ui);});
    [UnityTest] public IEnumerator GameplayCapturePersistsWhenMovingOverUi()=>UniTask.ToCoroutine(async()=>{await Start();var count=0;input.Subscribe(Owner(),_=>count++);source.Send(PointerPhase.Began);Pump();source.Send(PointerPhase.Moved,ui:true);Pump();source.Send(PointerPhase.Ended,ui:true);Pump();Assert.AreEqual(3,count);});
    [UnityTest] public IEnumerator OwnerCloseSuppressesFurtherCallbacks()=>UniTask.ToCoroutine(async()=>{await Start();var owner=Owner();var count=0;input.Subscribe(owner,_=>count++);source.Send(PointerPhase.Began);Pump();await owner.CloseAsync(Cleanup);source.Send(PointerPhase.Ended);Pump();Assert.AreEqual(1,count);Assert.AreEqual(0,input.ListenerCount);});
    [UnityTest] public IEnumerator RepeatedSubscribeCloseDoesNotGrowListeners()=>UniTask.ToCoroutine(async()=>{await Start();for(var i=0;i<100;i++){var owner=Owner();input.Subscribe(owner,_=>{});await owner.CloseAsync(Cleanup);}Assert.AreEqual(0,input.ListenerCount);});
    [UnityTest] public IEnumerator PauseImmediatelyCancelsActiveGesture()=>UniTask.ToCoroutine(async()=>{await Start();var phases=new List<PointerPhase>();input.Subscribe(Owner(),p=>phases.Add(p.Phase));source.Send(PointerPhase.Began);Pump();host.Host.SetPaused(true);Assert.IsTrue(input.IsPaused);CollectionAssert.AreEqual(new[]{PointerPhase.Began,PointerPhase.Canceled},phases);});
    [UnityTest] public IEnumerator PausedInputIsNotReplayedOnResume()=>UniTask.ToCoroutine(async()=>{await Start();var count=0;input.Subscribe(Owner(),_=>count++);host.Host.SetPaused(true);source.Send(PointerPhase.Began);Pump();host.Host.SetPaused(false);Pump();Assert.AreEqual(0,count);source.Send(PointerPhase.Began);Pump();Assert.AreEqual(1,count);});
    [UnityTest] public IEnumerator HeldPointerAfterResumeRequiresNewBegin()=>UniTask.ToCoroutine(async()=>{await Start();var count=0;input.Subscribe(Owner(),_=>count++);source.Send(PointerPhase.Began);Pump();host.Host.SetPaused(true);host.Host.SetPaused(false);source.Send(PointerPhase.Stationary);Pump();Assert.AreEqual(2,count);});
    [UnityTest] public IEnumerator UnsubscribeDuringDispatchIsSafe()=>UniTask.ToCoroutine(async()=>{await Start();IDisposable second=null;var count=0;input.Subscribe(Owner(),_=>second.Dispose());second=input.Subscribe(Owner(),_=>count++).Value;source.Send(PointerPhase.Began);Pump();Assert.AreEqual(0,count);});
    [UnityTest] public IEnumerator ListenerExceptionDoesNotBlockOtherListeners()=>UniTask.ToCoroutine(async()=>{await Start();var count=0;input.Subscribe(Owner(),_=>throw new InvalidOperationException("listener"));input.Subscribe(Owner(),_=>count++);source.Send(PointerPhase.Began);Pump();Assert.AreEqual(1,count);Assert.AreEqual(ErrorKind.Unexpected,input.LastError.Value.Kind);});
    [UnityTest] public IEnumerator NewListenerWaitsUntilNextFrame()=>UniTask.ToCoroutine(async()=>{await Start();var count=0;input.Subscribe(Owner(),p=>{if(p.Phase==PointerPhase.Began)input.Subscribe(Owner(),_=>count++);});source.Send(PointerPhase.Began);Pump();Assert.AreEqual(0,count);source.Send(PointerPhase.Moved);Pump();Assert.AreEqual(1,count);});
    [UnityTest] public IEnumerator DuplicateBeginIsRejected()=>UniTask.ToCoroutine(async()=>{await Start();var count=0;input.Subscribe(Owner(),_=>count++);source.Send(PointerPhase.Began);Pump();source.Send(PointerPhase.Began);Pump();Assert.AreEqual(1,count);Assert.AreEqual(ErrorKind.Integrity,input.LastError.Value.Kind);});
    [UnityTest] public IEnumerator InvalidFrameDoesNotReplaceLastMetrics()=>UniTask.ToCoroutine(async()=>{await Start();source.InvalidFrame=true;Pump();Assert.AreEqual(1000,input.Metrics.Width);Assert.AreEqual(ErrorKind.Integrity,input.LastError.Value.Kind);});
    [UnityTest] public IEnumerator CaptureExceptionIsObserved()=>UniTask.ToCoroutine(async()=>{await Start();source.Throw=true;Pump();Assert.AreEqual(ErrorKind.Provider,input.LastError.Value.Kind);source.Throw=false;});
    [UnityTest] public IEnumerator UnsupportedInputRollsBackHost()=>UniTask.ToCoroutine(async()=>{await Start(false);Assert.IsTrue((await context.CloseAsync(OperationContext.Unlimited())).IsSuccess);Assert.AreEqual(0,host.Host.TickCount);Assert.AreEqual(0,host.Host.PauseListenerCount);});
    [UnityTest] public IEnumerator StopRemovesHostSubscriptions()=>UniTask.ToCoroutine(async()=>{await Start();Assert.AreEqual(1,host.Host.TickCount);Assert.AreEqual(1,host.Host.PauseListenerCount);await context.CloseAsync(OperationContext.Unlimited());Assert.AreEqual(0,host.Host.TickCount);Assert.AreEqual(0,host.Host.PauseListenerCount);});
    [Test] public void SafeAreaClampsToDisplay(){var m=new DeviceMetrics(100,200,new ScreenArea(-10,20,130,300));Assert.AreEqual(0,m.SafeArea.X);Assert.AreEqual(100,m.SafeArea.Width);Assert.AreEqual(180,m.SafeArea.Height);}
    [Test] public void EmptySafeAreaFallsBackToFullDisplay(){var m=new DeviceMetrics(100,200,default);Assert.AreEqual(100,m.SafeArea.Width);Assert.AreEqual(200,m.SafeArea.Height);}
    [Test] public void CoordinatesUseBottomLeftNormalizedDisplay(){var point=new DeviceMetrics(100,200,new ScreenArea(0,0,100,200)).Normalize(new ScreenPoint(25,100));Assert.AreEqual(0.25f,point.X);Assert.AreEqual(0.5f,point.Y);}
    RectTransform CanvasChild(out Canvas canvas)
    {var go=new GameObject("S305Canvas",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler));objects.Add(go);canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;var child=new GameObject("SafeArea",typeof(RectTransform));objects.Add(child);child.transform.SetParent(go.transform,false);Canvas.ForceUpdateCanvases();return (RectTransform)child.transform;}
    [Test] public void SafeAreaAppliesNormalizedAnchorsWithoutRebuildingUi(){var child=CanvasChild(out _);Assert.IsTrue(InputUiCoordinates.ApplySafeArea(child,new DeviceMetrics(1000,500,new ScreenArea(50,20,900,460))).IsSuccess);Assert.AreEqual(new Vector2(0.05f,0.04f),child.anchorMin);Assert.AreEqual(new Vector2(0.95f,0.96f),child.anchorMax);Assert.AreEqual(Vector2.zero,child.offsetMin);Assert.AreEqual(Vector2.zero,child.offsetMax);}
    [Test] public void SafeAreaRejectsNestedTargetBeforeMutation(){var child=CanvasChild(out _);var nested=new GameObject("Nested",typeof(RectTransform));objects.Add(nested);nested.transform.SetParent(child,false);var rect=(RectTransform)nested.transform;var before=rect.anchorMin;Assert.IsFalse(InputUiCoordinates.ApplySafeArea(rect,new DeviceMetrics(100,100,new ScreenArea(0,0,100,100))).IsSuccess);Assert.AreEqual(before,rect.anchorMin);}
    [Test] public void ScreenToLocalUsesActualOverlayRectTransform(){var child=CanvasChild(out var canvas);var rect=(RectTransform)canvas.transform;var screen=new ScreenPoint(Screen.width/2f,Screen.height/2f);var converted=InputUiCoordinates.ScreenToLocal(rect,screen);Assert.IsTrue(converted.IsSuccess);Assert.That(converted.Value.magnitude,Is.LessThan(1));}
    [Test] public void WorldCanvasRequiresExplicitCamera(){var child=CanvasChild(out var canvas);canvas.renderMode=RenderMode.WorldSpace;canvas.worldCamera=null;Assert.IsFalse(InputUiCoordinates.ScreenToLocal(child,new ScreenPoint(1,1)).IsSuccess);}
    [Test] public void ScreenToLocalUsesExplicitCameraProjection(){var child=CanvasChild(out var canvas);var go=new GameObject("S305Camera",typeof(Camera));objects.Add(go);canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=go.GetComponent<Camera>();Canvas.ForceUpdateCanvases();Assert.IsTrue(InputUiCoordinates.ScreenToLocal(child,new ScreenPoint(Screen.width/2f,Screen.height/2f)).IsSuccess);}
    [UnityTest] public IEnumerator CaptureFailureCancelsActiveGesture()=>UniTask.ToCoroutine(async()=>{await Start();var phases=new List<PointerPhase>();input.Subscribe(Owner(),p=>phases.Add(p.Phase));source.Send(PointerPhase.Began);Pump();source.Throw=true;Pump();CollectionAssert.AreEqual(new[]{PointerPhase.Began,PointerPhase.Canceled},phases);source.Throw=false;source.Send(PointerPhase.Moved);Pump();Assert.AreEqual(2,phases.Count);});
    [UnityTest] public IEnumerator InvalidFrameCancelsActiveGesture()=>UniTask.ToCoroutine(async()=>{await Start();var phases=new List<PointerPhase>();input.Subscribe(Owner(),p=>phases.Add(p.Phase));source.Send(PointerPhase.Began);Pump();source.InvalidFrame=true;Pump();CollectionAssert.AreEqual(new[]{PointerPhase.Began,PointerPhase.Canceled},phases);source.InvalidFrame=false;});
    [UnityTest] public IEnumerator WarmInputMoveDispatchAllocatesNoManagedBytes()=>UniTask.ToCoroutine(async()=>{await Start();var count=0;input.Subscribe(Owner(),_=>count++);source.Send(PointerPhase.Began);Pump();source.RepeatMove=true;for(var i=0;i<100;i++)Pump();var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++)Pump();var allocated=GC.GetAllocatedBytesForCurrentThread()-before;Assert.AreEqual(0,allocated,"Scope: synthetic pointer construction + InputDevice dispatch + Host Pump; excludes Unity raycasters/device APIs.");Assert.AreEqual(1101,count);});
    [Test] public void UnityPointerSourceCapturesLiveDeviceMetrics(){var source=new UnityPointerSource();Assert.IsTrue(source.IsSupported);var frame=source.Capture(new PointerSample[16]);Assert.IsTrue(frame.IsSuccess);Assert.AreEqual(Screen.width,frame.Value.Metrics.Width);Assert.AreEqual(Screen.height,frame.Value.Metrics.Height);}
}
