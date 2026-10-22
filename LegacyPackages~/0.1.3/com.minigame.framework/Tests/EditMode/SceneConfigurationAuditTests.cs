using System;
using System.Linq;
using MiniGame.Framework.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MiniGame.Framework.Tests
{
    public sealed class SceneConfigurationAuditTests
    {
        private Scene scene;

        [SetUp]
        public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
        }

        [TearDown]
        public void Cleanup()
        {
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.ClosePreviewScene(scene);
        }

        private GameObject Create(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        [Test]
        public void InvalidAndEmptyScenesAreExplicit()
        {
            Assert.Throws<ArgumentException>(() => SceneConfigurationAudit.Inspect(default));
            Assert.IsTrue(SceneConfigurationAudit.Inspect(scene).Any(issue => issue.property == "roots"));
        }

        [Test]
        public void InteractiveCameraCanvasLocatesMissingInputAndCamera()
        {
            GameObject root = Create("CameraCanvas");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            root.AddComponent<GraphicRaycaster>();
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(float.NaN, 1280);
            using (var serialized = new UnityEditor.SerializedObject(canvas))
            {
                serialized.FindProperty("m_RenderMode").intValue = (int)RenderMode.ScreenSpaceCamera;
                serialized.FindProperty("m_Camera").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual((int)RenderMode.ScreenSpaceCamera, serialized.FindProperty("m_RenderMode").intValue);
            }
            Assert.IsTrue(canvas.enabled && root.activeInHierarchy, "Fixture requires an enabled active canvas.");
            Assert.IsFalse(canvas.worldCamera, "Fixture must not inherit a rendering camera.");
            var issues = SceneConfigurationAudit.Inspect(scene);
            string findings = string.Join("; ", issues.Select(issue => issue.path + "/" + issue.property));
            Assert.IsTrue(issues.Any(issue => issue.path == root.name && issue.property == "worldCamera"), findings);
            Assert.IsTrue(issues.Any(issue => issue.path == root.name && issue.property == "eventSystem"));
            Assert.IsTrue(issues.Any(issue => issue.path == root.name && issue.property == "referenceResolution"));
        }

        [Test]
        public void DuplicateEnabledOwnersAreFlaggedAndInactiveOnesAreIgnored()
        {
            var firstListener = Create("FirstListener").AddComponent<AudioListener>();
            var secondListener = Create("SecondListener").AddComponent<AudioListener>();
            var firstInput = Create("FirstInput").AddComponent<EventSystem>();
            var secondInput = Create("SecondInput").AddComponent<EventSystem>();
            var issues = SceneConfigurationAudit.Inspect(scene);
            Assert.IsTrue(issues.Any(issue => issue.property == "audioListeners"));
            Assert.IsTrue(issues.Any(issue => issue.property == "eventSystems"));
            secondListener.gameObject.SetActive(false);
            secondInput.enabled = false;
            issues = SceneConfigurationAudit.Inspect(scene);
            Assert.IsFalse(issues.Any(issue => issue.property == "audioListeners" || issue.property == "eventSystems"));
            Assert.IsTrue(firstListener.enabled && firstInput.enabled);
        }

        [Test]
        public void ValidLoadedSceneInspectionIsDeterministicAndDoesNotDirtyOrRebind()
        {
            GameObject root = Create("ValidCanvas");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasScaler>();
            Create("InputOwner").AddComponent<EventSystem>();
            bool wasDirty = scene.isDirty;
            int rootCount = scene.rootCount;
            int componentCount = root.GetComponents<Component>().Length;
            var first = SceneConfigurationAudit.Inspect(scene);
            var second = SceneConfigurationAudit.Inspect(scene);
            Assert.IsEmpty(first);
            CollectionAssert.AreEqual(first.Select(issue => issue.path + issue.property + issue.reason), second.Select(issue => issue.path + issue.property + issue.reason));
            Assert.AreEqual(wasDirty, scene.isDirty);
            Assert.AreEqual(rootCount, scene.rootCount);
            Assert.AreEqual(componentCount, root.GetComponents<Component>().Length);
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
        }
    }
}
