using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Access;
using MiniFramework.Audio;
using MiniFramework.Contracts;
using MiniFramework.Host;
using MiniFramework.Providers.Fake;
using MiniFramework.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
public sealed class AudioServiceTests
{
    static CleanupContext Cleanup=>new CleanupContext(default,Timeout.InfiniteTimeSpan);
    static OperationContext Wait=>OperationContext.Unlimited();
    static ResourceKey Key=>new ResourceKey("fake","main","v1","audio/test",typeof(AudioClip));
    static AudioPlayOptions Sfx=>new AudioPlayOptions(AudioChannel.Sfx);
    static AudioPlayOptions Bgm=>new AudioPlayOptions(AudioChannel.Bgm,true);
    sealed class Backend:IAudioBackend
    {
        internal readonly List<Voice> Voices=new List<Voice>();internal bool FailInit,FailShutdown,FailStart,FailClear,AutoClear=true;internal int Shutdowns;
        public Result Initialize()=>FailInit?Result.Failure(new FrameworkError(ErrorKind.Provider,"test.init")):Result.Success();
        public Result<IAudioVoice> CreateVoice(AudioClip clip,AudioPlayOptions options){var voice=new Voice(this,clip);Voices.Add(voice);return Result<IAudioVoice>.Success(voice);}
        public UniTask<Result> ShutdownAsync(CleanupContext cleanup){Shutdowns++;return UniTask.FromResult(FailShutdown?Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"test.shutdown")):Result.Success());}
        internal sealed class Voice:IAudioVoice
        {
            readonly Backend backend;internal AudioClip Clip;internal bool Started,Paused,Muted,Finished,FailResume;internal float Gain;internal int Clears;
            UniTaskCompletionSource<Result> clear;
            internal Voice(Backend backend,AudioClip clip){this.backend=backend;Clip=clip;}
            public bool HoldsClip=>Clip;
            public bool IsFinished=>Finished&&!Paused;
            public Result Start(){if(backend.FailStart)return Result.Failure(new FrameworkError(ErrorKind.Provider,"test.start"));Started=true;return Result.Success();}
            public Result SetPaused(bool paused){if(!paused&&FailResume)return Result.Failure(new FrameworkError(ErrorKind.Provider,"test.resume"));Paused=paused;return Result.Success();}
            public Result SetGain(float gain,bool muted){Gain=gain;Muted=muted;return Result.Success();}
            public UniTask<Result> ClearAsync(CleanupContext cleanup,bool retry)
            {
                if(!Clip)return UniTask.FromResult(Result.Success());
                if(clear!=null&&(clear.Task.Status==UniTaskStatus.Pending||!retry))return clear.Task;
                clear=new UniTaskCompletionSource<Result>();Clears++;if(backend.AutoClear)CompleteClear();return clear.Task;
            }
            internal void CompleteClear(){if(clear==null||clear.Task.Status!=UniTaskStatus.Pending)return;if(backend.FailClear)clear.TrySetResult(Result.Failure(new FrameworkError(ErrorKind.CleanupFailed,"test.clear")));else{Clip=null;Started=false;clear.TrySetResult(Result.Success());}}
        }
    }
    sealed class HostModule:IModule
    {
        internal readonly HostScheduler Host=new HostScheduler();public string Id=>"runtime-host";public IReadOnlyList<string> Dependencies=>Array.Empty<string>();
        public UniTask<Result> InitializeAsync(IModuleContext context,OperationContext operation)=>UniTask.FromResult(Result.Success());
        public UniTask<Result> StopAsync(CleanupContext cleanup)=>UniTask.FromResult(Result.Success());
        public UniTask<Result> DisposeAsync(CleanupContext cleanup){Host.Dispose();return UniTask.FromResult(Result.Success());}
    }
    sealed class Env
    {
        internal readonly FakeResourceProvider Provider=new FakeResourceProvider();internal readonly HostModule Host=new HostModule();internal readonly Backend Backend=new Backend();
        internal readonly UnityAudioBackend Native;internal readonly AssetsService Assets;internal readonly AudioService Audio;internal readonly GameContext Context;internal readonly AudioClip Clip;
        internal readonly List<Scope> Owners=new List<Scope>();internal bool AutoLoad=true,AutoRelease=true,FailLoad,FailRelease;
        internal Env(bool native=false,int capacity=32,bool failInit=false)
        {
            Backend.FailInit=failInit;Native=native?new UnityAudioBackend(capacity,"S305Audio"):null;Clip=AudioClip.Create("S305Clip",44100,1,44100,false);
            Assets=new AssetsService(Provider,"main","v1",Cleanup);
            Audio=new AudioService(Assets,native?(IAudioBackend)Native:Backend,Host.Host,Cleanup,capacity);
            Context=new GameContext(new[]{
                new ModuleRegistration(Audio.Id,Audio.Dependencies,()=>Audio,typeof(IAudioService)),
                new ModuleRegistration(Assets.Id,Assets.Dependencies,()=>Assets,typeof(IAssetsService)),
                new ModuleRegistration(Host.Id,Host.Dependencies,()=>Host)
            },Cleanup);
        }
        internal Scope Owner(){var scope=new Scope(Guid.NewGuid().ToString("N"),ScopeKind.Session);Owners.Add(scope);return scope;}
        internal void Step()
        {
            for(var i=0;i<Provider.AssetStartCount;i++)
            {
                var load=Provider.AssetAt<AudioClip>(i);
                if(AutoLoad&&load.Operation.State==BackendState.Running){if(FailLoad)load.Fail(new FrameworkError(ErrorKind.Provider,"test.load"));else load.Succeed(Clip);}
                if(AutoRelease&&load.Asset?.ReleaseAttempt?.State==BackendState.Running)load.Asset.ReleaseAttempt.Complete(FailRelease?Result<Unit>.Failure(new FrameworkError(ErrorKind.CleanupFailed,"test.release")):Result<Unit>.Success(default));
            }
            if(Backend.AutoClear)foreach(var voice in Backend.Voices)voice.CompleteClear();
        }
        internal async UniTask<T> Pump<T>(UniTask<T> task){for(var i=0;i<900&&task.Status==UniTaskStatus.Pending;i++){Step();await UniTask.Yield();}Assert.AreNotEqual(UniTaskStatus.Pending,task.Status,"audio operation did not finish");return await task;}
        internal async UniTask Frames(int count=8){for(var i=0;i<count;i++){Step();await UniTask.Yield();}}
        internal async UniTask Start(bool success=true){var result=await Pump(Context.StartAsync(Wait));Assert.AreEqual(success,result.IsSuccess,result.IsSuccess?"":result.Error.Code+": "+result.Error.Message);}
        internal UniTask<Result<IAudioPlayback>> Play(AudioPlayOptions? options=null,Scope owner=null,OperationContext? wait=null)=>Audio.PlayAsync(Key,options??Sfx,owner??Owner(),wait??Wait);
        internal async UniTask<IAudioPlayback> Playing(AudioPlayOptions? options=null,Scope owner=null){var r=await Pump(Play(options,owner));Assert.IsTrue(r.IsSuccess,r.IsSuccess?"":r.Error.Code);return r.Value;}
        internal async UniTask Finish()
        {
            AutoLoad=AutoRelease=true;FailLoad=FailRelease=false;Backend.FailClear=Backend.FailShutdown=false;Backend.AutoClear=true;
            await Pump(Context.CloseAsync(Wait));Assert.IsTrue((await Pump(Context.RetryCleanupAsync(Cleanup,Wait))).IsSuccess);
            foreach(var owner in Owners){await Pump(owner.CloseAsync(Cleanup));Assert.IsTrue((await Pump(owner.RetryFailedAsync(Cleanup))).IsFullyDrained);}
            Assert.AreEqual(0,Audio.RetainedPlaybackCount);Assert.AreEqual(0,Assets.RetainedEntryCount);Assert.AreEqual(0,Assets.RetainedRequestCount);
            UnityEngine.Object.DestroyImmediate(Clip);
        }
    }
    readonly List<Env> environments=new List<Env>();
    Env New(bool native=false,int capacity=32,bool failInit=false){var value=new Env(native,capacity,failInit);environments.Add(value);return value;}
    [UnityTearDown] public IEnumerator TearDown()=>UniTask.ToCoroutine(async()=>{foreach(var e in environments)await e.Finish();environments.Clear();});
    [UnityTest] public IEnumerator FacadePlaysOwnedAudioAfterReady()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var game=new GameServices(e.Context);var result=await e.Pump(game.PlayAudioAsync(Key,Sfx,e.Owner(),Wait));Assert.IsTrue(result.IsSuccess);Assert.AreEqual(AudioPlaybackState.Playing,result.Value.State);});
    [UnityTest] public IEnumerator WrongAssetTypeRejectedWithoutLoad()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var key=new ResourceKey("fake","main","v1","audio/test",typeof(TextAsset));Assert.AreEqual(ErrorKind.InvalidArgument,(await e.Audio.PlayAsync(key,Sfx,e.Owner(),Wait)).Error.Kind);Assert.AreEqual(0,e.Provider.AssetStartCount);});
    [UnityTest] public IEnumerator PreCanceledPlayDoesNotStartProvider()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();using(var c=new CancellationTokenSource()){c.Cancel();Assert.AreEqual(ErrorKind.Canceled,(await e.Play(wait:OperationContext.Unlimited(c.Token))).Error.Kind);Assert.AreEqual(0,e.Provider.AssetStartCount);}});
    [UnityTest] public IEnumerator ZeroDeadlineDoesNotStartProvider()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();Assert.AreEqual(ErrorKind.Timeout,(await e.Play(wait:default(OperationContext))).Error.Kind);Assert.AreEqual(0,e.Provider.AssetStartCount);});
    [UnityTest] public IEnumerator CancellationDuringLoadReclaimsLateClipWithoutVoice()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.AutoLoad=false;using(var c=new CancellationTokenSource()){var wait=e.Play(wait:OperationContext.Unlimited(c.Token));c.Cancel();Assert.IsFalse((await e.Pump(wait)).IsSuccess);Assert.AreEqual(0,e.Backend.Voices.Count);Assert.AreEqual(1,e.Assets.RetainedEntryCount);e.AutoLoad=true;await e.Frames(20);Assert.AreEqual(0,e.Audio.RetainedPlaybackCount);Assert.AreEqual(0,e.Assets.RetainedEntryCount);}});
    [UnityTest] public IEnumerator OwnerCloseDuringLoadWaitsActualCompletion()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.AutoLoad=false;var owner=e.Owner();var play=e.Play(owner:owner);var close=owner.CloseAsync(Cleanup);await e.Frames();Assert.AreEqual(UniTaskStatus.Pending,close.Status);e.AutoLoad=true;Assert.IsTrue((await e.Pump(close)).IsFullyDrained);Assert.IsFalse((await e.Pump(play)).IsSuccess);Assert.AreEqual(0,e.Backend.Voices.Count);});
    [UnityTest] public IEnumerator FailedNewMusicLoadKeepsOldMusic()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var old=await e.Playing(Bgm);var newKey=new ResourceKey("fake","main","v1","audio/missing",typeof(AudioClip));e.FailLoad=true;Assert.IsFalse((await e.Pump(e.Audio.PlayAsync(newKey,Bgm,e.Owner(),Wait))).IsSuccess);Assert.AreEqual(AudioPlaybackState.Playing,old.State);Assert.IsTrue(e.Backend.Voices[0].HoldsClip);});
    [UnityTest] public IEnumerator MusicReplacementStopsOldBeforeStartingNew()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var old=await e.Playing(Bgm);var next=await e.Playing(Bgm);Assert.AreEqual(AudioPlaybackState.Stopped,old.State);Assert.IsFalse(e.Backend.Voices[0].HoldsClip);Assert.IsTrue(e.Backend.Voices[1].Started);Assert.AreEqual(AudioPlaybackState.Playing,next.State);});
    [UnityTest] public IEnumerator PendingMusicReplacementRejectsAnotherMusicRequest()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.AutoLoad=false;var first=e.Play(Bgm);Assert.IsFalse((await e.Play(Bgm)).IsSuccess);e.AutoLoad=true;Assert.IsTrue((await e.Pump(first)).IsSuccess);});
    [UnityTest] public IEnumerator CapacityCountsPendingLoads()=>UniTask.ToCoroutine(async()=>{var e=New(capacity:1);await e.Start();e.AutoLoad=false;var first=e.Play();Assert.AreEqual(ErrorKind.Unavailable,(await e.Play()).Error.Kind);e.AutoLoad=true;Assert.IsTrue((await e.Pump(first)).IsSuccess);Assert.AreEqual(1,e.Provider.AssetStartCount);});
    [UnityTest] public IEnumerator MasterChannelAndPlaybackVolumesMultiply()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Audio.SetVolume(0.5f);e.Audio.SetChannelVolume(AudioChannel.Sfx,0.4f);await e.Playing(new AudioPlayOptions(AudioChannel.Sfx,false,0.5f));Assert.That(e.Backend.Voices[0].Gain,Is.EqualTo(0.1f).Within(0.00001));});
    [UnityTest] public IEnumerator MuteDoesNotStopOrReleaseClip()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var play=await e.Playing();e.Audio.SetMuted(true);Assert.IsTrue(e.Backend.Voices[0].Muted);Assert.IsTrue(e.Backend.Voices[0].HoldsClip);Assert.AreEqual(AudioPlaybackState.Playing,play.State);e.Audio.SetMuted(false);Assert.IsFalse(e.Backend.Voices[0].Muted);});
    [UnityTest] public IEnumerator ApplicationPauseIsImmediateWithoutTick()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var play=await e.Playing();e.Host.Host.SetPaused(true);Assert.IsTrue(e.Backend.Voices[0].Paused);Assert.AreEqual(AudioPlaybackState.Paused,play.State);e.Host.Host.SetPaused(false);Assert.IsFalse(e.Backend.Voices[0].Paused);});
    [UnityTest] public IEnumerator UserPauseSurvivesApplicationResume()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var play=await e.Playing();play.SetPaused(true);e.Host.Host.SetPaused(true);e.Host.Host.SetPaused(false);Assert.IsTrue(e.Backend.Voices[0].Paused);Assert.AreEqual(AudioPlaybackState.Paused,play.State);});
    [UnityTest] public IEnumerator ClipCompletedInBackgroundWaitsBeforeStartingVoice()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.AutoLoad=false;var play=e.Play();e.Host.Host.SetPaused(true);e.AutoLoad=true;await e.Frames();Assert.AreEqual(0,e.Backend.Voices.Count);Assert.AreEqual(UniTaskStatus.Pending,play.Status);e.Host.Host.SetPaused(false);Assert.IsTrue((await e.Pump(play)).IsSuccess);});
    [UnityTest] public IEnumerator TimeoutWhilePausedDoesNotStartOnResume()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Host.Host.SetPaused(true);var play=e.Play(wait:new OperationContext(default,TimeSpan.FromMilliseconds(1)));await UniTask.Delay(10,ignoreTimeScale:true);Assert.AreEqual(ErrorKind.Timeout,(await e.Pump(play)).Error.Kind);e.Host.Host.SetPaused(false);await e.Frames();Assert.AreEqual(0,e.Backend.Voices.Count);});
    [UnityTest] public IEnumerator NaturalCompletionClearsVoiceAndReturnsResource()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var play=await e.Playing();e.Backend.Voices[0].Finished=true;e.Host.Host.Pump(0.1f,0.1f);await e.Frames();Assert.AreEqual(AudioPlaybackState.Stopped,play.State);Assert.IsFalse(e.Backend.Voices[0].HoldsClip);Assert.AreEqual(0,e.Assets.RetainedEntryCount);});
    [UnityTest] public IEnumerator ClearFailureRetainsClipAndLeaseUntilExplicitRetry()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var play=await e.Playing();e.Backend.FailClear=true;Assert.IsFalse((await e.Pump(play.StopAsync(Cleanup))).IsSuccess);Assert.IsTrue(e.Backend.Voices[0].HoldsClip);Assert.AreEqual(1,e.Assets.RetainedEntryCount);e.Backend.FailClear=false;Assert.IsFalse((await play.StopAsync(Cleanup)).IsSuccess);Assert.IsTrue((await e.Pump(play.RetryStopAsync(Cleanup))).IsSuccess);Assert.AreEqual(0,e.Assets.RetainedEntryCount);});
    [UnityTest] public IEnumerator PendingClearDoesNotReleaseAssetEarly()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var play=await e.Playing();e.Backend.AutoClear=false;var stop=play.StopAsync(Cleanup);await e.Frames();Assert.AreEqual(UniTaskStatus.Pending,stop.Status);Assert.IsNull(e.Provider.AssetAt<AudioClip>(0).Asset.ReleaseAttempt);e.Backend.AutoClear=true;Assert.IsTrue((await e.Pump(stop)).IsSuccess);});
    [UnityTest] public IEnumerator AssetReleaseFailureRetainsTicketForExplicitRetry()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var play=await e.Playing();e.FailRelease=true;Assert.IsFalse((await e.Pump(play.StopAsync(Cleanup))).IsSuccess);Assert.IsFalse(e.Backend.Voices[0].HoldsClip);Assert.AreEqual(AudioPlaybackState.StopFailed,play.State);e.FailRelease=false;Assert.IsTrue((await e.Pump(play.RetryStopAsync(Cleanup))).IsSuccess);});
    [UnityTest] public IEnumerator ContextCloseOwnsPendingAudioLoad()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.AutoLoad=false;var play=e.Play();var close=e.Context.CloseAsync(Wait);await e.Frames();Assert.AreEqual(UniTaskStatus.Pending,close.Status);e.AutoLoad=true;Assert.IsTrue((await e.Pump(close)).IsSuccess);Assert.IsFalse((await e.Pump(play)).IsSuccess);Assert.AreEqual(0,e.Backend.Voices.Count);});
    [UnityTest] public IEnumerator StoppedPlaybackCannotControlReusedSource()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var old=await e.Playing();await e.Pump(old.StopAsync(Cleanup));var next=await e.Playing();Assert.IsFalse(old.SetPaused(true).IsSuccess);Assert.AreEqual(AudioPlaybackState.Playing,next.State);});
    [UnityTest] public IEnumerator ConcurrentStopSharesOneClearAttempt()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var play=await e.Playing();e.Backend.AutoClear=false;var first=play.StopAsync(Cleanup);var second=play.StopAsync(Cleanup);Assert.AreEqual(1,e.Backend.Voices[0].Clears);e.Backend.AutoClear=true;Assert.IsTrue((await e.Pump(first)).IsSuccess);Assert.IsTrue((await e.Pump(second)).IsSuccess);});
    [UnityTest] public IEnumerator RepeatedSuccessfulStopIsDetectable()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var play=await e.Playing();await e.Pump(play.StopAsync(Cleanup));Assert.IsFalse((await play.StopAsync(Cleanup)).IsSuccess);});
    [UnityTest] public IEnumerator BackendStartFailureCleansCreatedVoiceAndClip()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Backend.FailStart=true;Assert.AreEqual(ErrorKind.Provider,(await e.Pump(e.Play())).Error.Kind);await e.Frames();Assert.IsFalse(e.Backend.Voices[0].HoldsClip);Assert.AreEqual(0,e.Assets.RetainedEntryCount);});
    [UnityTest] public IEnumerator BackendInitializeFailureRollsBackCreatedModule()=>UniTask.ToCoroutine(async()=>{var e=New(failInit:true);await e.Start(false);Assert.IsTrue((await e.Pump(e.Context.CloseAsync(Wait))).IsSuccess);Assert.AreEqual(1,e.Backend.Shutdowns);});
    [UnityTest] public IEnumerator BackendShutdownFailureUsesContextRegistryRetry()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();e.Backend.FailShutdown=true;Assert.IsFalse((await e.Pump(e.Context.CloseAsync(Wait))).IsSuccess);e.Backend.FailShutdown=false;Assert.IsTrue((await e.Pump(e.Context.RetryCleanupAsync(Cleanup,Wait))).IsSuccess);Assert.AreEqual(2,e.Backend.Shutdowns);});
    [UnityTest] public IEnumerator UnitySourcePoolRemainsBoundedAcrossHundredPlays()=>UniTask.ToCoroutine(async()=>{var e=New(native:true,capacity:2);await e.Start();for(var i=0;i<100;i++){var play=await e.Playing();Assert.IsTrue((await e.Pump(play.StopAsync(Cleanup))).IsSuccess);}Assert.AreEqual(1,e.Native.SourceCount);Assert.AreEqual(0,e.Native.ActiveVoiceCount);Assert.AreEqual(0,e.Native.ClipReferenceCount);});
    [UnityTest] public IEnumerator UnitySourceClearsClipBeforeAssetReleaseConfirmation()=>UniTask.ToCoroutine(async()=>{var e=New(native:true);await e.Start();var play=await e.Playing();e.AutoRelease=false;var stop=play.StopAsync(Cleanup);await e.Frames();Assert.AreEqual(0,e.Native.ClipReferenceCount);Assert.AreEqual(1,e.Assets.RetainedEntryCount);Assert.AreEqual(UniTaskStatus.Pending,stop.Status);e.AutoRelease=true;Assert.IsTrue((await e.Pump(stop)).IsSuccess);});
    [UnityTest] public IEnumerator UnityPausedSourceKeepsClipAndCanResume()=>UniTask.ToCoroutine(async()=>{var e=New(native:true);await e.Start();var play=await e.Playing(Bgm);e.Host.Host.SetPaused(true);Assert.AreEqual(1,e.Native.ClipReferenceCount);Assert.AreEqual(AudioPlaybackState.Paused,play.State);Assert.IsFalse(e.Native.Root.GetComponent<AudioSource>().isPlaying);e.Host.Host.SetPaused(false);Assert.AreEqual(AudioPlaybackState.Playing,play.State);Assert.AreEqual(1,e.Native.ClipReferenceCount);});
    [UnityTest] public IEnumerator UnityBackendRootDestructionIsConfirmedOnContextClose()=>UniTask.ToCoroutine(async()=>{var e=New(native:true);await e.Start();Assert.IsNotNull(e.Native.Root);await e.Pump(e.Context.CloseAsync(Wait));Assert.IsNull(e.Native.Root);Assert.AreEqual(0,e.Native.SourceCount);});
    [UnityTest] public IEnumerator ConcurrentSameClipUsesSharedAssetAndIndependentVoices()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var a=e.Play();var b=e.Play();var first=(await e.Pump(a)).Value;var second=(await e.Pump(b)).Value;Assert.AreEqual(1,e.Provider.AssetStartCount);Assert.AreEqual(2,e.Backend.Voices.Count);await e.Pump(first.StopAsync(Cleanup));Assert.AreEqual(1,e.Assets.RetainedEntryCount);Assert.AreEqual(AudioPlaybackState.Playing,second.State);await e.Pump(second.StopAsync(Cleanup));Assert.AreEqual(0,e.Assets.RetainedEntryCount);});
    [UnityTest] public IEnumerator ClosingOneOwnerDoesNotStopAnotherVoice()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();var owner=e.Owner();var first=await e.Playing(owner:owner);var second=await e.Playing();await e.Pump(owner.CloseAsync(Cleanup));Assert.AreEqual(AudioPlaybackState.Stopped,first.State);Assert.AreEqual(AudioPlaybackState.Playing,second.State);});
    [UnityTest] public IEnumerator ResumeFailureIsObservedAndReclaimed()=>UniTask.ToCoroutine(async()=>{var e=New();await e.Start();await e.Playing();e.Host.Host.SetPaused(true);e.Backend.Voices[0].FailResume=true;e.Host.Host.SetPaused(false);Assert.AreEqual(ErrorKind.Provider,e.Audio.LastError.Value.Kind);await e.Frames();Assert.AreEqual(0,e.Audio.RetainedPlaybackCount);});
    [Test] public void AudioAssemblyHasNoOptionalSdkReferences(){Assert.IsFalse(typeof(AudioService).Assembly.GetReferencedAssemblies().Any(a=>a.Name.Contains("FMOD")||a.Name.Contains("Addressables")||a.Name.Contains("YooAsset")||a.Name.Contains("MiniGameFramework")));}
}
