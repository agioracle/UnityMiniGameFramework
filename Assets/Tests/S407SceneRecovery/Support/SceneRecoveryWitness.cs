using UnityEngine;
namespace MiniFramework.S407SceneRecovery
{
    // Fixture observation only. Activation during cleanup is not business Ready.
    public sealed class SceneRecoveryWitness : MonoBehaviour
    {
        public static int Enabled;
        void OnEnable() { Enabled++; }
    }
}
