using UnityEngine;

namespace MiniGame.Framework
{
    [DisallowMultipleComponent]
    public sealed class ResourceOwner : MonoBehaviour
    {
        private InstanceLease lease;
        internal void Attach(InstanceLease instanceLease) { lease = instanceLease; }
        private void OnDestroy()
        {
            lease?.ReleaseDestroyed();
            lease = null;
        }
    }
}
