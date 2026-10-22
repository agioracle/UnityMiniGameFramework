using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MiniGame.Framework
{
    public sealed class ConfigService
    {
        private readonly ResourceService resources;
        public ConfigService(ResourceService resources) { this.resources = resources ?? throw new ArgumentNullException(nameof(resources)); }
        public async UniTask<T> LoadAsync<T>(string key, Action<T> validate, CancellationToken cancellation = default)
        {
            if (validate == null) throw new ArgumentNullException(nameof(validate));
            using (AssetLease<TextAsset> lease = await resources.LoadAsync<TextAsset>(key, cancellation))
            {
                T value = JsonCodec.Decode<T>(lease.Asset.text);
                validate(value);
                cancellation.ThrowIfCancellationRequested();
                return value;
            }
        }
    }
}
