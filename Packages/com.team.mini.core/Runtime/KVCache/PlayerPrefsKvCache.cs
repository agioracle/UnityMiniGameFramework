using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;

namespace MiniFramework.KVCache
{
    // The only backend seam. Production always uses Unity PlayerPrefs.
    public interface IPlayerPrefsAccess
    {
        bool HasKey(string key);
        string GetString(string key);
        void SetString(string key, string value);
        void DeleteKey(string key);
        void Save();
    }

    public sealed class KvCacheOptions
    {
        public int MaxCachedKeys { get; }
        public int MaxCachedUtf8Bytes { get; }
        // Local cache budgets, not platform Storage quotas. No automatic eviction.
        public KvCacheOptions(int maxCachedKeys = 256, int maxCachedUtf8Bytes = 1048576)
        {
            if (maxCachedKeys < 1 || maxCachedUtf8Bytes < 1) throw new ArgumentOutOfRangeException();
            MaxCachedKeys = maxCachedKeys; MaxCachedUtf8Bytes = maxCachedUtf8Bytes;
        }
    }

    // Main-thread, single-writer ownership per namespace. No static state, timer or quit hook.
    public sealed class PlayerPrefsKvCache : IKvCache
    {
        sealed class Entry
        {
            public readonly string Raw;
            public readonly int Bytes;
            public bool Dirty;
            public bool Decoded;
            public char Type;
            public int Integer;
            public float Fraction;
            public string Text;
            public FrameworkError? DecodeError;
            public Entry(string raw, int bytes, bool dirty) { Raw = raw; Bytes = bytes; Dirty = dirty; }
        }
        readonly IPlayerPrefsAccess access;
        readonly KvCacheOptions options;
        readonly string prefix;
        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        long cachedBytes;
        int pending;
        bool busy;
        public int CachedKeyCount => entries.Count;
        public int PendingKeyCount => pending;
        public bool HasPendingChanges => pending != 0;

        public PlayerPrefsKvCache(string nameSpace, IPlayerPrefsAccess access, KvCacheOptions options = null)
        {
            if (!PlayerLoopHelper.IsMainThread) throw new InvalidOperationException("KV cache requires Unity main thread.");
            if (!StorageIdentifier.IsValid(nameSpace)) throw new ArgumentException("Portable namespace required.", nameof(nameSpace));
            this.access = access ?? throw new ArgumentNullException(nameof(access));
            this.options = options ?? new KvCacheOptions();
            prefix = "miniframework.kv.v1:" + nameSpace + ":";
        }

        static FrameworkError Error(string code, ErrorKind kind = ErrorKind.Storage) => new FrameworkError(kind, code);
        FrameworkError? Gate(string key)
        {
            if (!PlayerLoopHelper.IsMainThread) return Error("kv.thread", ErrorKind.InvalidState);
            if (busy) return Error("kv.busy", ErrorKind.InvalidState);
            if (key != null && StorageIdentifier.IsValid(key)) return null;
            return Error("kv.key.invalid", ErrorKind.InvalidArgument);
        }
        FrameworkError? ControlGate()
        {
            if (!PlayerLoopHelper.IsMainThread) return Error("kv.thread", ErrorKind.InvalidState);
            return busy ? Error("kv.busy", ErrorKind.InvalidState) : (FrameworkError?)null;
        }
        Result Put(string key, string raw, bool dirty)
        {
            entries.TryGetValue(key, out var old);
            // Count the physical key and encoded value; this is not a measurement of platform usage.
            long bytes = (long)Encoding.UTF8.GetByteCount(prefix + key) + (raw == null ? 0 : Encoding.UTF8.GetByteCount(raw));
            if (old == null && entries.Count >= options.MaxCachedKeys || bytes + cachedBytes - (old?.Bytes ?? 0) > options.MaxCachedUtf8Bytes)
                return Result.Failure(Error("kv.cache.budget", ErrorKind.Unavailable));
            if (old != null && old.Raw == raw) return Result.Success();
            if (old != null && old.Dirty) pending--;
            entries[key] = new Entry(raw, (int)bytes, dirty);
            cachedBytes += bytes - (old?.Bytes ?? 0);
            if (dirty) pending++;
            return Result.Success();
        }
        Result<Entry> Read(string key)
        {
            var gate = Gate(key); if (gate.HasValue) return Result<Entry>.Failure(gate.Value);
            if (entries.TryGetValue(key, out var entry)) return Result<Entry>.Success(entry);
            string raw;
            busy = true;
            try
            {
                var exists = access.HasKey(prefix + key);
                raw = exists ? access.GetString(prefix + key) : null;
                if (exists && raw == null) return Result<Entry>.Failure(Error("kv.value.invalid", ErrorKind.Integrity));
            }
            catch (Exception) { return Result<Entry>.Failure(Error("kv.playerprefs.read_failed")); }
            finally { busy = false; }
            // Existing null is invalid, never an absent key. Unity GetString normally returns non-null.
            var stored = Put(key, raw, false);
            return stored.IsSuccess ? Result<Entry>.Success(entries[key]) : Result<Entry>.Failure(stored.Error);
        }
        public Result<bool> HasKey(string key)
        {
            var read = Read(key);
            return read.IsSuccess ? Result<bool>.Success(read.Value.Raw != null) : Result<bool>.Failure(read.Error);
        }
        Result<Entry> Value(string key, char type)
        {
            var read = Read(key); if (!read.IsSuccess || read.Value.Raw == null) return read;
            var entry = read.Value;
            if (!entry.Decoded)
            {
                // Decode once per entry, including strings; hot reads do not repeat substring/parse.
                entry.Decoded = true;
                var raw = entry.Raw;
                if (raw.Length < 6 || !raw.StartsWith("kv1:", StringComparison.Ordinal) || raw[5] != ':' || "ifs".IndexOf(raw[4]) < 0)
                    entry.DecodeError = Error("kv.value.invalid", ErrorKind.Integrity);
                else
                {
                    entry.Type = raw[4];
                    var text = raw.Substring(6);
                    if (entry.Type == 's') entry.Text = text;
                    else if (entry.Type == 'i')
                    {
                        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out entry.Integer))
                            entry.DecodeError = Error("kv.value.invalid", ErrorKind.Integrity);
                    }
                    else if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out entry.Fraction) || float.IsNaN(entry.Fraction) || float.IsInfinity(entry.Fraction))
                        entry.DecodeError = Error("kv.value.invalid", ErrorKind.Integrity);
                }
            }
            if (entry.DecodeError.HasValue) return Result<Entry>.Failure(entry.DecodeError.Value);
            return entry.Type == type ? read : Result<Entry>.Failure(Error("kv.type_mismatch", ErrorKind.InvalidArgument));
        }
        public Result<int> GetInt(string key, int defaultValue = 0)
        {
            var value = Value(key, 'i'); if (!value.IsSuccess) return Result<int>.Failure(value.Error);
            return Result<int>.Success(value.Value.Raw == null ? defaultValue : value.Value.Integer);
        }
        public Result<float> GetFloat(string key, float defaultValue = 0)
        {
            var value = Value(key, 'f'); if (!value.IsSuccess) return Result<float>.Failure(value.Error);
            return Result<float>.Success(value.Value.Raw == null ? defaultValue : value.Value.Fraction);
        }
        public Result<string> GetString(string key, string defaultValue = "")
        {
            var value = Value(key, 's');
            return value.IsSuccess ? Result<string>.Success(value.Value.Raw == null ? defaultValue : value.Value.Text) : Result<string>.Failure(value.Error);
        }
        Result Set(string key, string raw)
        {
            var gate = Gate(key); return gate.HasValue ? Result.Failure(gate.Value) : Put(key, raw, true);
        }
        public Result SetInt(string key, int value) => Set(key, "kv1:i:" + value.ToString(CultureInfo.InvariantCulture));
        public Result SetFloat(string key, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return Result.Failure(Error("kv.float.invalid", ErrorKind.InvalidArgument));
            return Set(key, "kv1:f:" + value.ToString("R", CultureInfo.InvariantCulture));
        }
        public Result SetString(string key, string value) => value == null
            ? Result.Failure(Error("kv.string.null", ErrorKind.InvalidArgument)) : Set(key, "kv1:s:" + value);
        public Result DeleteKey(string key) => Set(key, null);

        public Result<KvFlushReceipt> Flush()
        {
            var gate = ControlGate(); if (gate.HasValue) return Result<KvFlushReceipt>.Failure(gate.Value);
            if (pending == 0) return Result<KvFlushReceipt>.Success(new KvFlushReceipt(0, false));
            busy = true;
            string phase = "write";
            try
            {
                foreach (var pair in entries)
                {
                    if (!pair.Value.Dirty) continue;
                    if (pair.Value.Raw == null) access.DeleteKey(prefix + pair.Key);
                    else access.SetString(prefix + pair.Key, pair.Value.Raw);
                }
                phase = "save";
                access.Save();
                var count = pending;
                foreach (var entry in entries.Values) entry.Dirty = false;
                pending = 0;
                return Result<KvFlushReceipt>.Success(new KvFlushReceipt(count, true));
            }
            catch (Exception)
            {
                // Earlier keys may already have reached the backend. Retain all dirty keys for
                // idempotent replay, never claim rollback, atomicity or platform durability.
                return Result<KvFlushReceipt>.Failure(Error("kv.playerprefs." + phase + "_failed"));
            }
            finally { busy = false; }
        }
        public Result InvalidateReadCache()
        {
            var gate = ControlGate(); if (gate.HasValue) return Result.Failure(gate.Value);
            var clean = new List<string>();
            foreach (var pair in entries) if (!pair.Value.Dirty) clean.Add(pair.Key);
            foreach (var key in clean) { cachedBytes -= entries[key].Bytes; entries.Remove(key); }
            return Result.Success();
        }
    }
}
