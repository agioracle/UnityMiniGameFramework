using UnityEngine;
namespace Game.Boot
{
    public sealed class SampleLevelActor:MonoBehaviour
    {
        public int TickCount {get;private set;}
        public void Tick(){TickCount++;transform.localRotation=Quaternion.Euler(0,0,Mathf.Sin(TickCount*0.02f)*20);}
    }
}
