using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public enum AudioChannel { Bgm, Sfx, Voice, Ambient, Ui }
    public enum AudioPlaybackState { Loading, Playing, Paused, Stopping, StopFailed, Stopped }
    public readonly struct AudioPlayOptions
    {
        public AudioChannel Channel {get;}public bool Loop {get;}public float Volume {get;}
        public AudioPlayOptions(AudioChannel channel,bool loop=false,float volume=1)
        {Channel=channel;Loop=loop;Volume=volume;}
    }
    public interface IAudioPlayback
    {
        string Id {get;}string OwnerId {get;}ResourceKey Key {get;}AudioChannel Channel {get;}AudioPlaybackState State {get;}
        Result SetPaused(bool paused);
        UniTask<Result> StopAsync(CleanupContext cleanup);
        UniTask<Result> RetryStopAsync(CleanupContext cleanup);
    }
    public interface IAudioService
    {
        UniTask<Result<IAudioPlayback>> PlayAsync(ResourceKey key,AudioPlayOptions options,IScope owner,OperationContext operation);
        Result SetVolume(float volume);
        Result SetChannelVolume(AudioChannel channel,float volume);
        Result SetMuted(bool muted);
    }
}
