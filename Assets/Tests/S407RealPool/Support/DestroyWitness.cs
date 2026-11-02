using System;
using UnityEngine;
namespace MiniFramework.S407RealPool
{
    // Added only to rented instances, activated by the fixture, never to the Resources prefab.
    public sealed class DestroyWitness : MonoBehaviour
    {
        public Action<int> Destroyed;
        void OnDestroy() => Destroyed?.Invoke(Time.frameCount);
    }
}
