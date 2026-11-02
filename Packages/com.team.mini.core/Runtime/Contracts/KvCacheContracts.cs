namespace MiniFramework.Contracts
{
    // A local preference cache, not an IStorageService transaction or a cloud save.
    public interface IKvCache
    {
        int CachedKeyCount { get; }
        int PendingKeyCount { get; }
        bool HasPendingChanges { get; }
        Result<bool> HasKey(string key);
        Result<int> GetInt(string key, int defaultValue = 0);
        Result<float> GetFloat(string key, float defaultValue = 0);
        Result<string> GetString(string key, string defaultValue = "");
        Result SetInt(string key, int value);
        Result SetFloat(string key, float value);
        Result SetString(string key, string value);
        Result DeleteKey(string key);
        Result<KvFlushReceipt> Flush();
        // Drops only clean read entries. Pending changes are never discarded.
        Result InvalidateReadCache();
    }

    public readonly struct KvFlushReceipt
    {
        public int AppliedKeyCount { get; }
        public bool SaveCalled { get; }
        // PlayerPrefs exposes no portable platform-persistence acknowledgement.
        public bool PlatformPersistenceConfirmed => false;
        public KvFlushReceipt(int appliedKeyCount, bool saveCalled)
        { AppliedKeyCount = appliedKeyCount; SaveCalled = saveCalled; }
    }
}
