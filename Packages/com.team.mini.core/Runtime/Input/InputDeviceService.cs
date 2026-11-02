using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Input
{
    public readonly struct PointerFrame
    {
        public DeviceMetrics Metrics {get;}public int Count {get;}
        public PointerFrame(DeviceMetrics metrics,int count){Metrics=metrics;Count=count;}
    }
    public interface IPointerSource
    {
        bool IsSupported {get;}
        Result<PointerFrame> Capture(PointerSample[] destination);
        void Reset();
    }
    public sealed class InputDeviceService:IModule,IInputDeviceService,IAsyncCleanup,IDiagnosticSource
    {
        readonly IPointerSource source;readonly IRuntimeScheduler host;readonly IApplicationLifecycle lifecycle;
        readonly PointerSample[] buffer;readonly Dictionary<int,PointerSample> gestures=new Dictionary<int,PointerSample>();
        readonly Dictionary<int,bool> uiCapture=new Dictionary<int,bool>();readonly List<Listener> listeners=new List<Listener>();
        IDisposable tick,pause;bool ready,stopped;int dispatchDepth;
        public string Id=>"input-device";public IReadOnlyList<string> Dependencies {get;}=Array.AsReadOnly(new[]{"runtime-host"});
        public DeviceMetrics Metrics {get;private set;}public bool IsPaused=>host.IsPaused;
        public FrameworkError? LastError {get;private set;}
        public int ListenerCount {get {var count=0;foreach(var l in listeners)if(l.Active)count++;return count;} }
        public InputDeviceService(IPointerSource source,IRuntimeScheduler host,int pointerCapacity=16)
        {this.source=source??throw new ArgumentNullException(nameof(source));this.host=host??throw new ArgumentNullException(nameof(host));lifecycle=host as IApplicationLifecycle??throw new ArgumentException("Host lifecycle notifications required.");if(pointerCapacity<=0)throw new ArgumentOutOfRangeException(nameof(pointerCapacity));buffer=new PointerSample[pointerCapacity];}
        static void Main(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("Input lifecycle requires main thread.");}
        static Result Fail(string code,ErrorKind kind=ErrorKind.InvalidState)=>Result.Failure(new FrameworkError(kind,code));
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            Main();if(operation.Cancellation.IsCancellationRequested)return UniTask.FromResult(Fail("input.initialize.canceled",ErrorKind.Canceled));
            if(operation.Timeout==TimeSpan.Zero)return UniTask.FromResult(Fail("input.initialize.timeout",ErrorKind.Timeout));
            if(ready||stopped)return UniTask.FromResult(Fail("input.initialize.state"));
            var ticket=context.Cleanup.Register(this);if(!ticket.IsSuccess)return UniTask.FromResult(Result.Failure(ticket.Error));
            if(!source.IsSupported)return UniTask.FromResult(Fail("input.backend.unsupported",ErrorKind.NotSupported));
            ready=true;source.Reset();pause=lifecycle.SubscribePause(OnPause);tick=host.SubscribeTick((_,__)=>Poll());Poll();return UniTask.FromResult(Result.Success());
        }
        public Result<IDisposable> Subscribe(IScope owner,Action<PointerSample> listener,bool includeUi=false)
        {
            Main();if(!ready||stopped)return Result<IDisposable>.Failure(new FrameworkError(ErrorKind.NotReady,"input.not_ready"));
            if(owner==null||owner.State!=ScopeState.Open||listener==null)return Result<IDisposable>.Failure(new FrameworkError(ErrorKind.InvalidArgument,"input.subscription.invalid"));
            var entry=new Listener(this,owner,listener,includeUi);var tracked=owner.TrackSubscription("input/"+Guid.NewGuid().ToString("N"),entry);
            if(!tracked.IsSuccess)return Result<IDisposable>.Failure(tracked.Error);listeners.Add(entry);return Result<IDisposable>.Success(entry);
        }
        void Poll()
        {
            if(!ready||stopped||IsPaused)return;
            try
            {
                var frame=source.Capture(buffer);
                if(!frame.IsSuccess){LastError=frame.Error;OnPause(true);return;}
                if(!frame.Value.Metrics.IsValid||frame.Value.Count<0||frame.Value.Count>buffer.Length){LastError=new FrameworkError(ErrorKind.Integrity,"input.frame.invalid");OnPause(true);return;}
                Metrics=frame.Value.Metrics;var limit=listeners.Count;
                dispatchDepth++;
                try
                {
                    for(var i=0;i<frame.Value.Count&&!stopped&&!IsPaused;i++)
                    {
                        var sample=buffer[i];
                        if(sample.Phase==PointerPhase.Began)
                        {if(gestures.ContainsKey(sample.Id)){LastError=new FrameworkError(ErrorKind.Integrity,"input.duplicate.begin");continue;}uiCapture.Add(sample.Id,sample.OverUi);}
                        else if(!gestures.ContainsKey(sample.Id))continue;
                        gestures[sample.Id]=sample;Dispatch(sample,uiCapture[sample.Id],limit);
                        if(sample.Phase==PointerPhase.Ended||sample.Phase==PointerPhase.Canceled){gestures.Remove(sample.Id);uiCapture.Remove(sample.Id);}
                    }
                }
                finally{dispatchDepth--;Compact();}
            }
            catch(Exception e){LastError=new FrameworkError(ErrorKind.Provider,"input.capture.exception",e.Message);OnPause(true);}
        }
        void Dispatch(PointerSample sample,bool blocked,int limit)
        {
            for(var i=0;i<limit&&!stopped;i++)
            {
                var listener=listeners[i];if(!listener.Active||listener.Owner.State!=ScopeState.Open||blocked&&!listener.IncludeUi)continue;
                try{listener.Callback(sample);}catch(Exception e){LastError=new FrameworkError(ErrorKind.Unexpected,"input.listener.exception",e.Message);}
            }
        }
        void OnPause(bool paused)
        {
            if(stopped)return;
            if(paused)
            {
                dispatchDepth++;
                try{var limit=listeners.Count;foreach(var sample in new List<PointerSample>(gestures.Values)){if(stopped)break;Dispatch(new PointerSample(sample.Id,PointerPhase.Canceled,sample.Position,sample.OverUi),uiCapture.TryGetValue(sample.Id,out var blocked)&&blocked,limit);}}
                finally{gestures.Clear();uiCapture.Clear();dispatchDepth--;Compact();}
            }
            else source.Reset();
        }
        void Compact(){if(dispatchDepth==0)listeners.RemoveAll(Dead);}
        static readonly Predicate<Listener> Dead=l=>!l.Active;
        sealed class Listener:IDisposable
        {
            readonly InputDeviceService service;internal readonly IScope Owner;internal Action<PointerSample> Callback;internal readonly bool IncludeUi;internal bool Active=true;
            internal Listener(InputDeviceService service,IScope owner,Action<PointerSample> callback,bool includeUi){this.service=service;Owner=owner;Callback=callback;IncludeUi=includeUi;}
            public void Dispose(){Main();Active=false;Callback=null;service.Compact();}
        }
        public UniTask<Result> StopAsync(CleanupContext context)
        {Main();if(stopped)return UniTask.FromResult(Result.Success());stopped=true;ready=false;tick?.Dispose();pause?.Dispose();foreach(var l in listeners){l.Active=false;l.Callback=null;}Compact();gestures.Clear();uiCapture.Clear();source.Reset();return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext context)=>UniTask.FromResult(Result.Success());
        public UniTask<Result> CleanupAsync(CleanupContext context)=>StopAsync(context);
        public DiagnosticCounters ReadDiagnostics()=>new DiagnosticCounters{Listeners=ListenerCount};
    }
}
