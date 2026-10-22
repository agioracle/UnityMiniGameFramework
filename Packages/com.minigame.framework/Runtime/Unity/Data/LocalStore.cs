using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MiniGame.Framework
{
    public enum StoreStatus { Success, Missing, Corrupt, NewerSchema, IoFailure, InvalidData }

    public readonly struct StoreResult<T>
    {
        public readonly StoreStatus Status;
        public readonly T Value;
        public readonly string Error;
        public bool Success => Status == StoreStatus.Success;
        public StoreResult(StoreStatus status, T value = default, string error = null) { Status = status; Value = value; Error = error; }
    }

    public sealed class LocalStore
    {
        [Serializable]
        private sealed class Envelope
        {
            public int schema;
            public long sequence;
            public string payload;
            public string hash;
        }

        private readonly string directory;
        private readonly int maximumBytes;
        public LocalStore(string directory, int maximumBytes = 1048576)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Directory required.", nameof(directory));
            if (maximumBytes < 128) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            this.directory = Path.GetFullPath(directory);
            this.maximumBytes = maximumBytes;
        }

        public StoreResult<T> Load<T>(string key, int schema, Func<string, int, T> migrate = null)
        {
            Validate(key, schema);
            try
            {
                Envelope latest = Latest(key, out bool exists);
                if (latest == null) return new StoreResult<T>(exists ? StoreStatus.Corrupt : StoreStatus.Missing);
                if (latest.schema > schema) return new StoreResult<T>(StoreStatus.NewerSchema);
                T value;
                if (latest.schema == schema) value = JsonCodec.Decode<T>(latest.payload, maximumBytes);
                else if (migrate != null) value = migrate(latest.payload, latest.schema);
                else return new StoreResult<T>(StoreStatus.InvalidData, error: "An explicit migration is required.");
                return new StoreResult<T>(StoreStatus.Success, value);
            }
            catch (IOException error) { return new StoreResult<T>(StoreStatus.IoFailure, error: error.GetType().Name); }
            catch (UnauthorizedAccessException error) { return new StoreResult<T>(StoreStatus.IoFailure, error: error.GetType().Name); }
            catch (Exception error) { return new StoreResult<T>(StoreStatus.InvalidData, error: error.GetType().Name); }
        }

        public StoreResult<bool> Save<T>(string key, int schema, T value)
        {
            Validate(key, schema);
            try
            {
                Envelope latest = Latest(key, out bool exists);
                if (latest == null && exists) return new StoreResult<bool>(StoreStatus.Corrupt);
                if (latest != null && latest.schema > schema) return new StoreResult<bool>(StoreStatus.NewerSchema);
                long sequence = latest == null ? 1 : checked(latest.sequence + 1);
                string payload = JsonCodec.Encode(value, maximumBytes);
                var envelope = new Envelope { schema = schema, sequence = sequence, payload = payload };
                envelope.hash = Hash(envelope);
                string json = JsonUtility.ToJson(envelope);
                if (Encoding.UTF8.GetByteCount(json) > maximumBytes * 2L) return new StoreResult<bool>(StoreStatus.InvalidData);
                Directory.CreateDirectory(directory);
                File.WriteAllText(PathFor(key, sequence % 2 == 0 ? "a" : "b"), json, new UTF8Encoding(false));
                return new StoreResult<bool>(StoreStatus.Success, true);
            }
            catch (IOException error) { return new StoreResult<bool>(StoreStatus.IoFailure, error: error.GetType().Name); }
            catch (UnauthorizedAccessException error) { return new StoreResult<bool>(StoreStatus.IoFailure, error: error.GetType().Name); }
            catch (Exception error) { return new StoreResult<bool>(StoreStatus.InvalidData, error: error.GetType().Name); }
        }

        private Envelope Latest(string key, out bool exists)
        {
            string a = PathFor(key, "a"), b = PathFor(key, "b");
            exists = File.Exists(a) || File.Exists(b);
            Envelope first = Read(a), second = Read(b);
            if (first == null) return second;
            if (second == null) return first;
            return first.sequence >= second.sequence ? first : second;
        }
        private Envelope Read(string path)
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > maximumBytes * 2L) return null;
            string text = File.ReadAllText(path, Encoding.UTF8);
            try
            {
                Envelope envelope = JsonUtility.FromJson<Envelope>(text);
                return envelope != null && envelope.schema > 0 && envelope.sequence > 0 && envelope.payload != null &&
                       Encoding.UTF8.GetByteCount(envelope.payload) <= maximumBytes && envelope.hash == Hash(envelope) ? envelope : null;
            }
            catch (ArgumentException) { return null; }
        }
        private static string Hash(Envelope envelope)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(envelope.schema.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + envelope.sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + envelope.payload);
                return Convert.ToBase64String(sha.ComputeHash(bytes));
            }
        }
        private string PathFor(string key, string suffix) => Path.Combine(directory, key + "." + suffix + ".json");
        private static void Validate(string key, int schema)
        {
            if (schema < 1) throw new ArgumentOutOfRangeException(nameof(schema));
            if (string.IsNullOrWhiteSpace(key) || key.Length > 80) throw new ArgumentException("Invalid key.", nameof(key));
            foreach (char c in key)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-'))
                    throw new ArgumentException("Keys may contain only letters, digits, underscore and hyphen.", nameof(key));
        }
    }
}
