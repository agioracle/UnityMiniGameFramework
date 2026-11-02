using System;
using MiniFramework.Contracts;
using UnityEngine;
namespace MiniFramework.UI
{
    // Content ownership belongs to the existing Pool/Assets ledger, never a UI SDK cache.
    public interface IPanelSource
    {
        Result<IOwnedOperation<IPoolRent<GameObject>>> Rent(ResourceKey prefab,IScope viewScope,ResourcePriority priority=ResourcePriority.Normal);
    }
    public sealed class PooledPanelSource : IPanelSource
    {
        private readonly IPoolService<GameObject> pool;
        public PooledPanelSource(IPoolService<GameObject> pool){this.pool=pool??throw new ArgumentNullException(nameof(pool));}
        public Result<IOwnedOperation<IPoolRent<GameObject>>> Rent(ResourceKey prefab,IScope viewScope,ResourcePriority priority=ResourcePriority.Normal)
        {
            if(!prefab.IsValid||prefab.Kind!=ResourceKind.Asset||prefab.AssetType!=typeof(GameObject))return Result<IOwnedOperation<IPoolRent<GameObject>>>.Failure(new FrameworkError(ErrorKind.InvalidArgument,"ui.prefab.key"));
            return pool.Spawn(prefab,viewScope,priority);
        }
    }
}
