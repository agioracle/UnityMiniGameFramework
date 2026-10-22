#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using MiniGame.Framework.Reactive;
using NUnit.Framework;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;


namespace MiniGame.Framework.Tests
{
    public sealed class FrameworkGapTests
    {
        [UnityTest]
        public IEnumerator ClearDuringMultiValueSynchronousSubscribeStopsOldRendering()
        {
            var root = new GameObject("Synchronous subscription gap");
            try
            {
                var display = root.AddComponent<DisplayLifetime>();
                int calls = 0;
                var source = Observable.Create<int>(observer =>
                {
                    observer.OnNext(1);
                    observer.OnNext(2);
                    return new ReleaseProbe();
                });
                display.Bind(source, _ => { calls++; display.Clear(); });
                Assert.AreEqual(1, calls, "The second synchronous value belongs to a cleared display generation.");
                yield return null;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator RepeatedCoverStateStillCorrectsActualActivationWithoutDuplicateEvents()
        {
            var root = new GameObject("Covered cached panel", typeof(RectTransform), typeof(PanelView));
            try
            {
                var panel = root.GetComponent<PanelView>();
                int changes = 0;
                panel.CoveredChanged += _ => changes++;
                var method = typeof(PanelView).GetMethod("SetCovered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                method.Invoke(panel, new object[] { true });
                Assert.IsFalse(root.activeSelf);
                root.SetActive(true);
                method.Invoke(panel, new object[] { true });
                Assert.IsFalse(root.activeSelf, "Unchanged covered state must still correct activation after cached reopening.");
                Assert.AreEqual(1, changes);
                method.Invoke(panel, new object[] { false });
                Assert.IsTrue(root.activeSelf);
                Assert.AreEqual(2, changes);
                yield return null;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator MissingGlyphSelectsCoveringFallbackWithoutMutatingFontAssets()
        {
            var root = new GameObject("Localized glyph gap", typeof(RectTransform), typeof(TextMeshProUGUI));
            TMP_FontAsset primary = Font("Primary probe", 'A');
            TMP_FontAsset fallback = Font("Fallback probe", 'A', 'B');
            try
            {
                var label = root.GetComponent<TextMeshProUGUI>();
                label.richText = false;
                label.font = primary;
                var binding = root.AddComponent<LocalizedTextBinding>();
                var localization = new LocalizationService("en");
                localization.AddLanguage("en", new Dictionary<string, string> { { "title", "A" } });
                localization.AddLanguage("el", new Dictionary<string, string> { { "title", "AB" } });
                int originalPrimaryCount = primary.characterTable.Count;
                binding.Bind(localization, "title", new Dictionary<string, TMP_FontAsset> { { "en", primary }, { "el", primary } }, fallback);
                Assert.AreSame(primary, label.font);
                localization.SetLanguage("el");
                Assert.AreSame(fallback, label.font, "A mapped font without the requested glyph must use the provided covering fallback.");
                root.SetActive(false);
                localization.SetLanguage("en");
                root.SetActive(true);
                Assert.AreSame(primary, label.font);
                binding.Clear();
                Assert.AreSame(primary, label.font);
                Assert.AreEqual(originalPrimaryCount, primary.characterTable.Count);
                Assert.IsTrue(primary.fallbackFontAssetTable == null || primary.fallbackFontAssetTable.Count == 0);
                yield return null;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(primary);
                UnityEngine.Object.DestroyImmediate(fallback);
            }
        }

        private static TMP_FontAsset Font(string name, params char[] characters)
        {
            var original = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UnityEditor.AssetDatabase.GUIDToAssetPath("ac99418e97c3f4c24873bfc6ac40c727"));
            Assert.IsTrue(original, "Requires the existing licensed sample font fixture.");
            var font = UnityEngine.Object.Instantiate(original);
            font.name = name;
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            Assert.AreNotSame(original.characterTable, font.characterTable);
            font.characterTable.RemoveAll(character => Array.IndexOf(characters, (char)character.unicode) < 0);
            font.ReadFontAssetDefinition();
            return font;
        }

        [UnityTest]
        public IEnumerator BackgroundBindingAndClearAreRejectedBeforeAccessingUnityObjects()
        {
            var root = new GameObject("Main thread display contract");
            try
            {
                var display = root.AddComponent<DisplayLifetime>();
                using (var source = new Subject<int>())
                {
                    var task = System.Threading.Tasks.Task.Run(() =>
                    {
                        Assert.Throws<InvalidOperationException>(() => display.Bind(source, _ => { }));
                        Assert.Throws<InvalidOperationException>(() => display.Clear());
                    });
                    while (!task.IsCompleted) yield return null;
                    task.GetAwaiter().GetResult();
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private sealed class ReleaseProbe : IDisposable
        {
            public void Dispose() { }
        }
    }
}
#endif
