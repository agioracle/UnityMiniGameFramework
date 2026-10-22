using System.Linq;
using MiniGame.Framework.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MiniGame.Framework.Tests
{
    public sealed class SceneReferenceBoundaryTests
    {
        private Scene owner;
        private Scene other;

        [SetUp]
        public void Setup()
        {
            owner = EditorSceneManager.NewPreviewScene();
            other = EditorSceneManager.NewPreviewScene();
        }

        [TearDown]
        public void Cleanup()
        {
            if (owner.IsValid() && owner.isLoaded) EditorSceneManager.ClosePreviewScene(owner);
            if (other.IsValid() && other.isLoaded) EditorSceneManager.ClosePreviewScene(other);
        }

        private static GameObject Create(Scene scene, string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        [Test]
        public void CrossSceneCameraReferenceIsReportedWithoutChangingOwnership()
        {
            var canvas = Create(owner, "CameraCanvas").AddComponent<Canvas>();
            var camera = Create(other, "ExternalCamera").AddComponent<Camera>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            using (var serialized = new SerializedObject(canvas))
                Assert.AreSame(camera, serialized.FindProperty("m_Camera").objectReferenceValue, "Fixture must contain a real cross-scene reference.");
            bool originalOwnerDirty = owner.isDirty, originalOtherDirty = other.isDirty;
            var issues = SceneConfigurationAudit.Inspect(owner);
            Assert.IsTrue(issues.Any(issue => issue.property == "crossSceneReferences"));
            Assert.IsFalse(issues.Any(issue => issue.property == "worldCamera"));
            Assert.AreSame(camera, canvas.worldCamera);
            Assert.AreEqual(other, camera.gameObject.scene);
            Assert.AreEqual(originalOwnerDirty, owner.isDirty);
            Assert.AreEqual(originalOtherDirty, other.isDirty);
        }

        [Test]
        public void LocalCameraReferenceIsNotMisreportedAsCrossScene()
        {
            var canvas = Create(owner, "LocalCanvas").AddComponent<Canvas>();
            var camera = Create(owner, "LocalCamera").AddComponent<Camera>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            var issues = SceneConfigurationAudit.Inspect(owner);
            Assert.IsFalse(issues.Any(issue => issue.property == "crossSceneReferences" || issue.property == "worldCamera"));
            Assert.AreSame(camera, canvas.worldCamera);
        }

        [Test]
        public void SharedInputOwnerInAnotherSceneRequiresExplicitReview()
        {
            GameObject root = Create(owner, "SharedInputCanvas");
            root.AddComponent<Canvas>();
            root.AddComponent<GraphicRaycaster>();
            Create(other, "SharedInputOwner").AddComponent<EventSystem>();
            var issues = SceneConfigurationAudit.Inspect(owner);
            Assert.IsTrue(issues.Any(issue => issue.path == root.name && issue.property == "eventSystem" && issue.reason.Contains("shared additive-scene input owner")));
            Assert.IsFalse(issues.Any(issue => issue.property == "eventSystems"));
            Assert.AreEqual(1, other.rootCount);
        }
    }
}
