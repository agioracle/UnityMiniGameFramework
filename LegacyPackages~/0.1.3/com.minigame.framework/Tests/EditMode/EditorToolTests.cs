using System;
using System.Linq;
using MiniGame.Framework.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Framework.Tests
{
    public sealed class EditorToolTests
    {
        [Test]
        public void PublicApiSnapshotIsStableAndContainsNoBusinessTypes()
        {
            string[] first = FrameworkAudit.PublicApi();
            string[] second = FrameworkAudit.PublicApi();
            CollectionAssert.AreEqual(first, second);
            Assert.IsTrue(first.Any(entry => entry.Contains("MiniGame.Framework.ResourceService")));
            Assert.IsFalse(first.Any(entry => entry.Contains("MiniGame.Examples") || entry.Contains("MiniGame.Business")));
        }

        [Test]
        public void BindingAuditDoesNotMutateAValidObject()
        {
            var root = new GameObject("AuditRoot");
            try
            {
                root.AddComponent<AudioSource>();
                int count = root.GetComponents<Component>().Length;
                var result = FrameworkAudit.InspectBindings(root);
                Assert.AreEqual(0, result.Count);
                Assert.AreEqual(count, root.GetComponents<Component>().Length);
                Assert.AreEqual("AuditRoot", root.name);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void PlayerBuildRequiresExplicitScenesAndProtectedOutput()
        {
            Assert.Throws<ArgumentException>(() => PlayerBuilder.Build(Array.Empty<string>(), BuildTarget.StandaloneOSX, "Builds/Players/Test.app"));
            Assert.Throws<ArgumentException>(() => PlayerBuilder.Build(new[] { "Assets/Scenes/SampleScene.unity" }, BuildTarget.NoTarget, "Builds/Players/Test.app"));
            Assert.Throws<ArgumentException>(() => BuildPathGuard.RequireChild("Builds", "Assets/Outside.app"));
            Assert.DoesNotThrow(() => BuildPathGuard.RequireChild("Builds", "Builds/Players/New.app"));
        }

        [Test]
        public void BuildRejectsTraversalAndOutputOutsideBuilds()
        {
            Assert.Throws<ArgumentException>(() => ContentBuilder.Build("../outside", "A", BuildTarget.StandaloneOSX, "Builds/Test"));
            Assert.Throws<ArgumentException>(() => ContentBuilder.Build("Package", "../A", BuildTarget.StandaloneOSX, "Builds/Test"));
            Assert.Throws<ArgumentException>(() => ContentBuilder.Build("Package", "A", BuildTarget.StandaloneOSX, Application.dataPath));
            Assert.Throws<ArgumentException>(() => ContentBuilder.Build("Package", "A", BuildTarget.NoTarget, "Builds/Test"));
        }
    }
}
