using System;
using System.Text;
using UnityEngine;

namespace MiniGame.Framework
{
    public static class JsonCodec
    {
        public static string Encode<T>(T value, int maximumBytes = 1048576)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            string json = JsonUtility.ToJson(value);
            Validate(json, maximumBytes);
            return json;
        }

        public static T Decode<T>(string json, int maximumBytes = 1048576)
        {
            Validate(json, maximumBytes);
            T result = JsonUtility.FromJson<T>(json);
            if (result == null) throw new FormatException("JSON payload did not produce an object.");
            return result;
        }

        private static void Validate(string json, int maximumBytes)
        {
            if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("JSON payload is empty.");
            if (Encoding.UTF8.GetByteCount(json) > maximumBytes) throw new ArgumentException("JSON payload exceeds the configured limit.");
        }
    }
}
