using UnityEngine;

namespace MiniFramework.KVCache.Unity
{
    public static class UnityPlayerPrefsCache
    {
        // Construct on Unity's main thread. The composition root owns the returned cache and Flush.
        public static PlayerPrefsKvCache Create(string nameSpace, KvCacheOptions options = null)
            => new PlayerPrefsKvCache(nameSpace, new Access(), options);

        sealed class Access : IPlayerPrefsAccess
        {
            public bool HasKey(string key) => PlayerPrefs.HasKey(key);
            public string GetString(string key) => PlayerPrefs.GetString(key);
            public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
            public void DeleteKey(string key) => PlayerPrefs.DeleteKey(key);
            public void Save() => PlayerPrefs.Save();
        }
    }
}
