using System;
using System.Collections.Generic;
using System.Globalization;

namespace MiniGame.Framework
{
    public sealed class LocalizationService
    {
        private readonly Dictionary<string, Dictionary<string, string>> languages = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        private readonly string fallback;
        public string Language { get; private set; }
        public event Action LanguageChanged;

        public LocalizationService(string fallbackLanguage)
        {
            if (string.IsNullOrWhiteSpace(fallbackLanguage)) throw new ArgumentException("Fallback language required.");
            fallback = fallbackLanguage;
            Language = fallback;
        }
        public void AddLanguage(string language, IReadOnlyDictionary<string, string> entries)
        {
            if (string.IsNullOrWhiteSpace(language)) throw new ArgumentException("Language required.");
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in entries)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null) throw new ArgumentException("Invalid localization entry.");
                copy.Add(pair.Key, pair.Value);
            }
            languages[language] = copy;
        }
        public bool SetLanguage(string language)
        {
            if (language == null || !languages.ContainsKey(language)) return false;
            if (Language == language) return true;
            Language = language;
            LanguageChanged?.Invoke();
            return true;
        }
        public string Get(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (languages.TryGetValue(Language, out var current) && current.TryGetValue(key, out string value)) return value;
            if (languages.TryGetValue(fallback, out var defaults) && defaults.TryGetValue(key, out value)) return value;
            return "[" + key + "]";
        }
        public string Format(string key, params object[] values) => string.Format(CultureInfo.InvariantCulture, Get(key), values);
    }
}
