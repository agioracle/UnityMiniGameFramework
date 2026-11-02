using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using UnityEngine;
namespace MiniFramework.Audio
{
    public sealed class AudioService:IAudioService,IModule,IAsyncCleanup,IDiagnosticSource
    {
        readonly IAssetsService assets;readonly IAudioBackend backend;readonly IRuntimeScheduler host;readonly IApplicationLifecycle lifecycle;
        readonly CleanupContext cleanup;readonly int capacity;readonly List<Playback> requests=new List<Playback>();
        readonly float[] volumes={1,1,1,1,1};float volume=1;bool muted,ready,stopped,ticking,cleanupAttempted;IDisposable tick,pause;
        Playback pendingBgm;
        public string Id=>"audio";public IReadOnlyList<string> Dependencies {get;}
        public FrameworkError? LastError {get;private set;}
        public int RetainedPlaybackCount=>requests.Count;
        public AudioService(IAssetsService assets,IAudioBackend backend,IRuntimeScheduler host,CleanupContext cleanup,int capacity=32,string assetsModuleId="assets")
        {
            this.assets=assets??throw new ArgumentNullException(nameof(assets));this.backend=backend??throw new ArgumentNullException(nameof(backend));this.host=host??throw new ArgumentNullException(nameof(host));lifecycle=host as IApplicationLifecycle??throw new ArgumentException("Host lifecycle required.");
            if(capacity<=0||string.IsNullOrWhiteSpace(assetsModuleId))throw new ArgumentException("Audio capacity/dependency required.");this.capacity=capacity;this.cleanup=cleanup;
            Dependencies=Array.AsReadOnly(new[]{assetsModuleId,"runtime-host"});
        }
        internal static void Main(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("Audio lifecycle requires main thread.");}
        internal static Result Fail(string code,ErrorKind kind=ErrorKind.InvalidState)=>Result.Failure(new FrameworkError(kind,code));
        internal static Result<T> Fail<T>(string code,ErrorKind kind=ErrorKind.InvalidState)=>Result<T>.Failure(new FrameworkError(kind,code));
        static bool Gain(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value)&&value>=0&&value<=1;
        static FrameworkError? Interrupt(OperationContext operation,long start)
        {
            if(operation.Cancellation.IsCancellationRequested)return new FrameworkError(ErrorKind.Canceled,"audio.wait.canceled");
            if(operation.Timeout!=Timeout.InfiniteTimeSpan&&(Stopwatch.GetTimestamp()-start)/(double)Stopwatch.Frequency>=operation.Timeout.TotalSeconds)return new FrameworkError(ErrorKind.Timeout,"audio.wait.timeout");
            return null;
        }
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)
        {
            Main();var error=Interrupt(operation,Stopwatch.GetTimestamp());if(error.HasValue)return UniTask.FromResult(Result.Failure(error.Value));
            if(ready||stopped)return UniTask.FromResult(Fail("audio.initialize.state"));
            var registered=context.Cleanup.Register(this);if(!registered.IsSuccess)return UniTask.FromResult(Result.Failure(registered.Error));
            var result=backend.Initialize();if(!result.IsSuccess)return UniTask.FromResult(result);
            ready=true;pause=lifecycle.SubscribePause(OnPause);tick=host.SubscribeTick((_,__)=>Poll());return UniTask.FromResult(Result.Success());
        }
        public UniTask<Result<IAudioPlayback>> PlayAsync(ResourceKey key,AudioPlayOptions options,IScope owner,OperationContext operation)
        {
            Main();var start=Stopwatch.GetTimestamp();var error=Interrupt(operation,start);
            if(error.HasValue)return UniTask.FromResult(Result<IAudioPlayback>.Failure(error.Value));
            if(!ready||stopped)return UniTask.FromResult(Fail<IAudioPlayback>("audio.not_ready",ErrorKind.NotReady));
            if(owner==null||owner.State!=ScopeState.Open||!key.IsValid||key.Kind!=ResourceKind.Asset||key.AssetType!=typeof(AudioClip)||!Enum.IsDefined(typeof(AudioChannel),options.Channel)||!Gain(options.Volume))return UniTask.FromResult(Fail<IAudioPlayback>("audio.play.arguments",ErrorKind.InvalidArgument));
            var oldBgm=false;foreach(var r in requests)if(r.Channel==AudioChannel.Bgm&&!r.Drained)oldBgm=true;
            if(options.Channel==AudioChannel.Bgm&&pendingBgm!=null)return UniTask.FromResult(Fail<IAudioPlayback>("audio.bgm.busy"));
            if(requests.Count>=capacity&&!(options.Channel==AudioChannel.Bgm&&oldBgm))return UniTask.FromResult(Fail<IAudioPlayback>("audio.capacity",ErrorKind.Unavailable));
            var playback=new Playback(this,key,options,owner,operation,start);
            var ticket=owner.Cleanup.Register(new PlaybackCleanup(playback));
            if(!ticket.IsSuccess)return UniTask.FromResult(Result<IAudioPlayback>.Failure(ticket.Error));
            requests.Add(playback);if(options.Channel==AudioChannel.Bgm)pendingBgm=playback;
            playback.StartLoading();return playback.Observe();
        }
        public Result SetVolume(float value){Main();if(!ready||stopped)return Fail("audio.not_ready",ErrorKind.NotReady);if(!Gain(value))return Fail("audio.volume",ErrorKind.InvalidArgument);volume=value;return Refresh();}
        public Result SetChannelVolume(AudioChannel channel,float value){Main();if(!ready||stopped)return Fail("audio.not_ready",ErrorKind.NotReady);if(!Enum.IsDefined(typeof(AudioChannel),channel)||!Gain(value))return Fail("audio.channel.volume",ErrorKind.InvalidArgument);volumes[(int)channel]=value;return Refresh();}
        public Result SetMuted(bool value){Main();if(!ready||stopped)return Fail("audio.not_ready",ErrorKind.NotReady);muted=value;return Refresh();}
        Result Refresh()
        {
            var result=Result.Success();foreach(var r in requests)if(r.Voice!=null&&!r.Closing){var gain=r.Refresh();if(!gain.IsSuccess){LastError=gain.Error;result=gain;}}return result;
        }
        void OnPause(bool paused)
        {
            if(stopped)return;
            foreach(var request in requests.ToArray())if(request.Voice!=null&&!request.Closing)
            {var result=request.RefreshPause();if(!result.IsSuccess){LastError=result.Error;request.Close(cleanup,false);}}
        }
        void Poll()
        {
            if(stopped||host.IsPaused)return;ticking=true;
            try
            {
                var limit=requests.Count;for(var i=0;i<limit;i++)
                {
                    var request=requests[i];if(request.Closing||request.Voice==null)continue;
                    try{if(request.Voice.IsFinished)request.Close(cleanup,false);}
                    catch(Exception e){LastError=new FrameworkError(ErrorKind.Provider,"audio.voice.poll",e.Message);request.Close(cleanup,false);}
                }
            }
            finally{ticking=false;Compact();}
        }
        void Compact(){if(!ticking)requests.RemoveAll(Completed);}
        static readonly Predicate<Playback> Completed=r=>r.Drained;
        void Remove(Playback request){request.Drained=true;if(pendingBgm==request)pendingBgm=null;Compact();}
        public UniTask<Result> StopAsync(CleanupContext context)
        {Main();if(stopped)return UniTask.FromResult(Result.Success());stopped=true;ready=false;tick?.Dispose();pause?.Dispose();foreach(var r in requests.ToArray())r.Close(context,false);return UniTask.FromResult(Result.Success());}
        public UniTask<Result> DisposeAsync(CleanupContext context)=>UniTask.FromResult(Result.Success());
        public async UniTask<Result> CleanupAsync(CleanupContext context)
        {
            Main();await StopAsync(context);var retry=cleanupAttempted;cleanupAttempted=true;var success=true;
            foreach(var r in requests.ToArray())if(!(await r.Close(context,retry)).IsSuccess)success=false;
            if(!success)return Fail("audio.cleanup.retained",ErrorKind.CleanupFailed);
            return await backend.ShutdownAsync(context);
        }
        public DiagnosticCounters ReadDiagnostics(){int failures=0,pending=0;foreach(var r in requests){if(r.CleanupFailed)failures++;if(!r.LoadSettled)pending++;}return new DiagnosticCounters{PendingOperations=pending,FailedCleanup=failures,ActiveRentals=requests.Count};}
        sealed class PlaybackCleanup:IAsyncCleanup
        {
            readonly Playback playback;bool attempted;
            internal PlaybackCleanup(Playback playback){this.playback=playback;}public string Id=>playback.Id;
            public UniTask<Result> CleanupAsync(CleanupContext context){var retry=attempted;attempted=true;return playback.Close(context,retry);}
        }
        sealed class Playback:IAudioPlayback
        {
            readonly AudioService service;readonly IScope owner;readonly AudioPlayOptions options;readonly OperationContext operation;readonly long started;
            readonly Scope resources;readonly CancellationTokenSource lifetime=new CancellationTokenSource();
            readonly UniTaskCompletionSource<Result<IAudioPlayback>> loaded=new UniTaskCompletionSource<Result<IAudioPlayback>>();
            UniTaskCompletionSource<Result> closed;UniTask loadDriver,closeDriver;bool observing=true,abandoned,userPaused,scopeClosing,lifetimeDisposed;
            internal IAudioVoice Voice;internal bool Drained;internal bool Closing=>closed!=null;
            internal bool LoadSettled=>loaded.Task.Status!=UniTaskStatus.Pending;
            internal bool CleanupFailed=>closed!=null&&closed.Task.Status!=UniTaskStatus.Pending&&!closed.Task.GetAwaiter().GetResult().IsSuccess;
            public string Id {get;}="audio/"+Guid.NewGuid().ToString("N");public string OwnerId=>owner.Id;public ResourceKey Key {get;}public AudioChannel Channel=>options.Channel;
            public AudioPlaybackState State=>Drained?AudioPlaybackState.Stopped:CleanupFailed?AudioPlaybackState.StopFailed:Closing?AudioPlaybackState.Stopping:!LoadSettled?AudioPlaybackState.Loading:userPaused||service.host.IsPaused?AudioPlaybackState.Paused:AudioPlaybackState.Playing;
            bool Invalid=>abandoned||service.stopped||owner.State!=ScopeState.Open||(observing&&Interrupt(operation,started).HasValue);
            internal Playback(AudioService service,ResourceKey key,AudioPlayOptions options,IScope owner,OperationContext operation,long started)
            {this.service=service;Key=key;this.options=options;this.owner=owner;this.operation=operation;this.started=started;resources=new Scope(Id+"/clip",ScopeKind.Rent);}
            internal void StartLoading(){loadDriver=Load().Preserve();}
            internal async UniTask<Result<IAudioPlayback>> Observe()
            {
                while(true)
                {
                    var error=Interrupt(operation,started);
                    if(error.HasValue||service.stopped||owner.State!=ScopeState.Open){abandoned=true;Close(service.cleanup,false);return error.HasValue?Result<IAudioPlayback>.Failure(error.Value):Fail<IAudioPlayback>("audio.owner.closed",ErrorKind.Canceled);}
                    if(LoadSettled){observing=false;return await loaded.Task;}if(abandoned)return Fail<IAudioPlayback>("audio.stopped",ErrorKind.Canceled);await UniTask.Yield();
                }
            }
            async UniTask Load()
            {
                Result<IAudioPlayback> result;
                try
                {
                    var request=service.assets.Load<AudioClip>(Key,resources);
                    if(!request.IsSuccess)throw new AudioFailure(request.Error);
                    var clip=await request.Value.WaitAsync(OperationContext.Unlimited(lifetime.Token));await UniTask.SwitchToMainThread();
                    if(!clip.IsSuccess)throw new AudioFailure(clip.Error);
                    while(service.host.IsPaused&&!Invalid)await UniTask.Yield();
                    if(Invalid)throw new AudioFailure(new FrameworkError(ErrorKind.Canceled,"audio.play.abandoned"));
                    if(Channel==AudioChannel.Bgm)
                    {
                        foreach(var old in service.requests.ToArray())
                            if(old!=this&&old.Channel==AudioChannel.Bgm){var stopped=await old.Close(service.cleanup,false);if(!stopped.IsSuccess)throw new AudioFailure(stopped.Error);}
                    }
                    if(Invalid)throw new AudioFailure(new FrameworkError(ErrorKind.Canceled,"audio.play.abandoned"));
                    var created=service.backend.CreateVoice(clip.Value.Value,options);if(!created.IsSuccess)throw new AudioFailure(created.Error);Voice=created.Value;
                    if(Voice==null)throw new AudioFailure(new FrameworkError(ErrorKind.Integrity,"audio.voice.null"));
                    var gain=Refresh();if(!gain.IsSuccess)throw new AudioFailure(gain.Error);
                    var start=Voice.Start();if(!start.IsSuccess)throw new AudioFailure(start.Error);
                    var paused=RefreshPause();if(!paused.IsSuccess)throw new AudioFailure(paused.Error);
                    result=Result<IAudioPlayback>.Success(this);
                }
                catch(AudioFailure error){result=Result<IAudioPlayback>.Failure(error.Error);}
                catch(Exception error){result=Result<IAudioPlayback>.Failure(new FrameworkError(ErrorKind.Provider,"audio.play.exception",error.Message));}
                if(service.pendingBgm==this)service.pendingBgm=null;
                loaded.TrySetResult(result);
                if(!result.IsSuccess||Invalid){abandoned=true;Close(service.cleanup,false);}
            }
            internal Result Refresh()=>Voice.SetGain(options.Volume*service.volume*service.volumes[(int)Channel],service.muted);
            internal Result RefreshPause()=>Voice.SetPaused(userPaused||service.host.IsPaused);
            public Result SetPaused(bool paused){Main();if(Closing||!LoadSettled||Voice==null||Invalid)return Fail("audio.pause.state");userPaused=paused;return RefreshPause();}
            public UniTask<Result> StopAsync(CleanupContext context)
            {Main();return Drained||CleanupFailed?UniTask.FromResult(Fail("audio.stop.state")):Close(context,false);}
            public UniTask<Result> RetryStopAsync(CleanupContext context)
            {Main();return !CleanupFailed?UniTask.FromResult(Fail("audio.retry.state")):Close(context,true);}
            internal UniTask<Result> Close(CleanupContext context,bool retry)
            {
                Main();abandoned=true;if(!lifetimeDisposed)lifetime.Cancel();
                if(closed!=null&&(closed.Task.Status==UniTaskStatus.Pending||!retry||closed.Task.GetAwaiter().GetResult().IsSuccess))return closed.Task;
                closed=new UniTaskCompletionSource<Result>();closeDriver=CloseActual(context,retry,closed).Preserve();return closed.Task;
            }
            async UniTask CloseActual(CleanupContext context,bool retry,UniTaskCompletionSource<Result> completion)
            {
                Result result;
                try
                {
                    await loaded.Task;
                    if(Voice!=null)
                    {
                        var cleared=await Voice.ClearAsync(context,retry);if(!cleared.IsSuccess)throw new AudioFailure(cleared.Error);
                        if(Voice.HoldsClip)throw new AudioFailure(new FrameworkError(ErrorKind.Integrity,"audio.voice.clip.retained"));
                    }
                    var report=scopeClosing?await resources.RetryFailedAsync(context):await resources.CloseAsync(context);scopeClosing=true;
                    while(report.PendingCount>0){await UniTask.Yield();report=resources.Snapshot;}
                    if(!report.IsFullyDrained)throw new AudioFailure(new FrameworkError(ErrorKind.CleanupFailed,"audio.asset.retained"));
                    if(!lifetimeDisposed){lifetimeDisposed=true;lifetime.Dispose();}service.Remove(this);result=Result.Success();
                }
                catch(AudioFailure error){result=Result.Failure(error.Error);}
                catch(Exception error){result=Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"audio.cleanup.exception",error.Message));}
                if(!result.IsSuccess)service.LastError=result.Error;completion.TrySetResult(result);
            }
        }
        sealed class AudioFailure:Exception {internal readonly FrameworkError Error;internal AudioFailure(FrameworkError error){Error=error;}}
    }
}
