using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using UnityEngine;
namespace MiniFramework.Audio
{
    // Playback knows clips/sources, not addresses, asset caches or external audio SDKs.
    public interface IAudioBackend
    {
        Result Initialize();
        Result<IAudioVoice> CreateVoice(AudioClip clip,AudioPlayOptions options);
        UniTask<Result> ShutdownAsync(CleanupContext cleanup);
    }
    public interface IAudioVoice
    {
        bool HoldsClip {get;}bool IsFinished {get;}
        Result Start();Result SetPaused(bool paused);Result SetGain(float volume,bool muted);
        UniTask<Result> ClearAsync(CleanupContext cleanup,bool retry);
    }
    public sealed class UnityAudioBackend:IAudioBackend
    {
        readonly int capacity;readonly string name;readonly List<AudioSource> sources=new List<AudioSource>();readonly Stack<AudioSource> idle=new Stack<AudioSource>();
        GameObject root;bool initialized,stopped;int active;
        public int ActiveVoiceCount=>active;public int SourceCount=>sources.Count;public Transform Root=>root?root.transform:null;
        public int ClipReferenceCount {get {var count=0;foreach(var source in sources)if(source&&source.clip)count++;return count;} }
        public UnityAudioBackend(int capacity=32,string name="MiniFramework.Audio")
        {if(capacity<=0||string.IsNullOrWhiteSpace(name))throw new ArgumentException("Positive audio capacity and name required.");this.capacity=capacity;this.name=name;}
        public Result Initialize()
        {
            AudioService.Main();if(initialized||stopped)return AudioService.Fail("audio.backend.initialize.state");
            root=new GameObject(name);initialized=true;return Result.Success();
        }
        public Result<IAudioVoice> CreateVoice(AudioClip clip,AudioPlayOptions options)
        {
            AudioService.Main();if(!initialized||stopped||!root)return AudioService.Fail<IAudioVoice>("audio.backend.not_ready",ErrorKind.NotReady);
            if(!clip)return AudioService.Fail<IAudioVoice>("audio.backend.clip",ErrorKind.Integrity);
            AudioSource source=null;while(idle.Count>0&&!source)source=idle.Pop();
            if(!source)
            {
                if(sources.Count>=capacity)return AudioService.Fail<IAudioVoice>("audio.backend.capacity",ErrorKind.Unavailable);
                source=root.AddComponent<AudioSource>();source.playOnAwake=false;sources.Add(source);
            }
            source.Stop();source.clip=clip;source.loop=options.Loop;source.volume=options.Volume;source.mute=false;source.spatialBlend=0;source.pitch=1;active++;
            return Result<IAudioVoice>.Success(new Voice(this,source));
        }
        public async UniTask<Result> ShutdownAsync(CleanupContext cleanup)
        {
            AudioService.Main();if(active!=0)return AudioService.Fail("audio.backend.voices.retained",ErrorKind.CleanupFailed);
            stopped=true;if(root)UnityEngine.Object.Destroy(root);
            while(root)await UniTask.Yield();sources.Clear();idle.Clear();return Result.Success();
        }
        sealed class Voice:IAudioVoice
        {
            readonly UnityAudioBackend owner;AudioSource source;bool started,paused,cleared;
            internal Voice(UnityAudioBackend owner,AudioSource source){this.owner=owner;this.source=source;}
            public bool HoldsClip=>!cleared&&source&&source.clip;
            public bool IsFinished=>!cleared&&started&&!paused&&(!source||!source.loop&&!source.isPlaying);
            public Result Start(){if(cleared||!source||!source.clip)return AudioService.Fail("audio.voice.start.state");source.Play();started=true;return Result.Success();}
            public Result SetPaused(bool value)
            {
                if(cleared||!source)return AudioService.Fail("audio.voice.pause.state");
                if(value==paused)return Result.Success();if(value)source.Pause();else if(started)source.UnPause();paused=value;return Result.Success();
            }
            public Result SetGain(float volume,bool muted){if(cleared||!source)return AudioService.Fail("audio.voice.gain.state");source.volume=volume;source.mute=muted;return Result.Success();}
            public UniTask<Result> ClearAsync(CleanupContext cleanup,bool retry)
            {
                AudioService.Main();if(cleared)return UniTask.FromResult(Result.Success());
                if(source){source.Stop();source.clip=null;source.loop=false;source.mute=false;owner.idle.Push(source);}
                source=null;cleared=true;owner.active--;return UniTask.FromResult(Result.Success());
            }
        }
    }
}
