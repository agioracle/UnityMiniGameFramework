using System.Threading;
using UnityEngine;
namespace MiniFramework.Host
{
    [DisallowMultipleComponent]
    public sealed class RuntimeHost : MonoBehaviour
    {
        private static int sessionEpoch;
        private int epoch;
        private bool applicationPaused, skipDelta;
        public HostScheduler Scheduler { get; private set; }
        public bool IsRunning => Scheduler != null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() { Interlocked.Increment(ref sessionEpoch); }
        public HostScheduler StartHost()
        {
            if(epoch!=sessionEpoch){StopHost();applicationPaused=false;}
            if(Scheduler==null){Scheduler=new HostScheduler();epoch=sessionEpoch;Scheduler.SetPaused(applicationPaused);skipDelta=true;}
            return Scheduler;
        }
        public void StopHost() { var scheduler=Scheduler;scheduler?.Dispose();Scheduler=null; }
        private void OnEnable() { StartHost(); }
        private void OnDisable() { StopHost(); }
        private void OnDestroy() { StopHost(); }
        private void OnApplicationPause(bool paused) { applicationPaused=paused;if(!paused)skipDelta=true;Scheduler?.SetPaused(paused); }
        private void Update()
        {
            if(Scheduler==null)return;
            var scheduler=StartHost();
            var scaled=skipDelta?0:Time.deltaTime;var unscaled=skipDelta?0:Time.unscaledDeltaTime;skipDelta=false;
            scheduler.Pump(scaled,unscaled);
        }
    }
}
