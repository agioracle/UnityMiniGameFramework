using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace MiniGame.Framework
{
    [RequireComponent(typeof(TMP_Text))]
    public sealed class LocalizedTextBinding : MonoBehaviour
    {
        [SerializeField] private string textKey;
        private TMP_Text label;
        private TMP_FontAsset originalFont;
        private TMP_FontAsset fallbackFont;
        private Dictionary<string, TMP_FontAsset> fonts;
        private LocalizationService localization;
        private bool subscribed;

        private void Awake()
        {
            label = GetComponent<TMP_Text>();
            originalFont = label.font;
        }

        public void Bind(LocalizationService service, string key, IReadOnlyDictionary<string, TMP_FontAsset> fontMap, TMP_FontAsset fallback)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Text key required.", nameof(key));
            if (fontMap == null) throw new ArgumentNullException(nameof(fontMap));
            if (!fallback) throw new ArgumentNullException(nameof(fallback));
            var copy = new Dictionary<string, TMP_FontAsset>(StringComparer.Ordinal);
            foreach (var pair in fontMap)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || !pair.Value) throw new ArgumentException("Font mappings require a language and a valid font.");
                copy.Add(pair.Key, pair.Value);
            }
            Clear();
            localization = service;
            textKey = key;
            fonts = copy;
            fallbackFont = fallback;
            if (isActiveAndEnabled) Subscribe();
        }

        public void Clear()
        {
            Unsubscribe();
            localization = null;
            fonts = null;
            fallbackFont = null;
            if (label) label.font = originalFont;
        }

        private void Subscribe()
        {
            if (localization == null || subscribed) return;
            subscribed = true;
            localization.LanguageChanged += Render;
            Render();
        }
        private void Unsubscribe()
        {
            if (subscribed && localization != null) localization.LanguageChanged -= Render;
            subscribed = false;
        }
        private void Render()
        {
            if (!label || localization == null) return;
            string text = localization.Get(textKey);
            TMP_FontAsset font = fonts.TryGetValue(localization.Language, out var mapped) && mapped ? mapped : fallbackFont;
            if (font != fallbackFont && !Covers(font, text) && Covers(fallbackFont, text)) font = fallbackFont;
            label.font = font;
            label.text = text;
        }

        private static bool Covers(TMP_FontAsset font, string text)
        {
            if (!font) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i])) continue;
                int character = text[i];
                if (char.IsHighSurrogate(text[i]))
                {
                    if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                    character = char.ConvertToUtf32(text[i], text[++i]);
                }
                else if (char.IsLowSurrogate(text[i])) return false;
                if (!font.HasCharacter(character)) return false;
            }
            return true;
        }
        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();
        private void OnDestroy() => Clear();
    }
}
