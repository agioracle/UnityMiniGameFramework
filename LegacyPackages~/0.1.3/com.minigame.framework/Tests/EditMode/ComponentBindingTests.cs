using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using MiniGame.Framework.Editor;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Framework.Tests
{
    public sealed class ComponentBindingTests
    {
        private GameObject root;
        [SetUp]
        public void Setup() { root = new GameObject("Binding fixture"); }
        [TearDown]
        public void Cleanup() { UnityEngine.Object.DestroyImmediate(root); }
        private ComponentBindingGenerator.Binding[] Bindings()
        {
            AudioSource audio = root.GetComponent<AudioSource>();
            if (!audio) audio = root.AddComponent<AudioSource>();
            Assert.IsTrue(audio, "Binding fixture requires a real Unity AudioSource.");
            return new[]
            {
                new ComponentBindingGenerator.Binding { fieldName = "anchor", component = root.transform },
                new ComponentBindingGenerator.Binding { fieldName = "soundSource", component = audio }
            };
        }
        [Test]
        public void GeneratedSourceIsDeterministicAndHasNoRuntimeSearch()
        {
            var bindings = Bindings();
            int componentCount = root.GetComponents<Component>().Length;
            string source = ComponentBindingGenerator.GenerateSource("Tests.Generated", "ExplicitBindings", bindings);
            Assert.AreEqual(source, ComponentBindingGenerator.GenerateSource("Tests.Generated", "ExplicitBindings", bindings));
            StringAssert.Contains("private global::UnityEngine.AudioSource soundSource;", source);
            StringAssert.Contains("ValidateBindings()", source);
            StringAssert.DoesNotContain("Find(", source);
            StringAssert.DoesNotContain("GetComponent", source);
            Assert.AreEqual(componentCount, root.GetComponents<Component>().Length);
        }
        [Test]
        public void InvalidIdentifiersDuplicateFieldsAndInheritedMembersAreRejected()
        {
            var bindings = Bindings();
            Assert.Throws<ArgumentException>(() => ComponentBindingGenerator.GenerateSource("Tests.Generated", "class", bindings));
            Assert.Throws<ArgumentException>(() => ComponentBindingGenerator.GenerateSource("Tests.Generated", "anchor", bindings));
            Assert.Throws<ArgumentException>(() => ComponentBindingGenerator.GenerateSource("Tests.Generated", "ValidateBindings", bindings));
            Assert.Throws<ArgumentException>(() => ComponentBindingGenerator.GenerateSource("Tests;Injected", "View", bindings));
            bindings[1].fieldName = "anchor";
            Assert.Throws<ArgumentException>(() => ComponentBindingGenerator.GenerateSource("Tests.Generated", "View", bindings));
            bindings[1].fieldName = "enabled";
            Assert.Throws<ArgumentException>(() => ComponentBindingGenerator.GenerateSource("Tests.Generated", "View", bindings));
            Assert.Throws<ArgumentException>(() => ComponentBindingGenerator.SaveNew("Builds/View.cs", "Tests.Generated", "View", Bindings()));
        }

        [Test]
        public void ExistingFilesAndPersistentPrefabAssetsAreProtected()
        {
            var bindings = Bindings();
            string directory = "Assets/BindingProtection-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(directory);
            string path = directory + "/ProtectedBindings.cs";
            const string original = "// Existing user-owned file.\n";
            File.WriteAllText(path, original);
            try
            {
                Assert.Throws<IOException>(() => ComponentBindingGenerator.SaveNew(path, "Tests.Generated", "ProtectedBindings", bindings));
                Assert.AreEqual(original, File.ReadAllText(path));
                string prefabPath = directory + "/Protected.prefab";
                var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Component target = prefab.GetComponent<AudioSource>();
                Assert.IsTrue(UnityEditor.EditorUtility.IsPersistent(target));
                Assert.Throws<ArgumentException>(() => ComponentBindingGenerator.Apply(target, new[]
                {
                    new ComponentBindingGenerator.Binding { fieldName = "anchor", component = prefab.transform }
                }));
            }
            finally
            {
                UnityEditor.AssetDatabase.DeleteAsset(directory);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [UnityTest]
        public IEnumerator GeneratedSourceCompilesAndExplicitReferencesValidate()
        {
            var bindings = Bindings();
            string name = "CompiledBindings" + Guid.NewGuid().ToString("N");
            string directory = Path.GetFullPath("Builds/Reports/binding-compile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string source = Path.Combine(directory, name + ".cs");
            File.WriteAllText(source, ComponentBindingGenerator.GenerateSource("Tests.Generated", name, bindings));
            string output = Path.Combine(directory, name + ".dll");
            var builder = new AssemblyBuilder(output, new[] { source }) { flags = AssemblyBuilderFlags.EditorAssembly };
            CompilerMessage[] messages = null;
            builder.buildFinished += (_, result) => messages = result;
            Assert.IsTrue(builder.Build());
            float deadline = Time.realtimeSinceStartup + 60;
            while (messages == null && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsNotNull(messages, "Generated-source compilation did not complete; evidence directory retained.");
            string errors = string.Join("\n", messages.Where(message => message.type == CompilerMessageType.Error).Select(message => message.message));
            Assert.IsEmpty(errors);
            Assert.IsTrue(File.Exists(output));
            Type type = System.Reflection.Assembly.LoadFile(output).GetType("Tests.Generated." + name, true);
            Component target = root.AddComponent(type);
            Assert.IsNotNull(target);
            MethodInfo validate = type.GetMethod("ValidateBindings");
            Assert.Throws<TargetInvocationException>(() => validate.Invoke(target, null));
            UnityEditor.Undo.IncrementCurrentGroup();
            ComponentBindingGenerator.Apply(target, bindings);
            Assert.DoesNotThrow(() => validate.Invoke(target, null));
            UnityEditor.Undo.FlushUndoRecordObjects();
            UnityEditor.Undo.PerformUndo();
            Assert.Throws<TargetInvocationException>(() => validate.Invoke(target, null));
            ComponentBindingGenerator.Apply(target, bindings);
            Assert.DoesNotThrow(() => validate.Invoke(target, null));
            Assert.AreSame(root.transform, type.GetField("anchor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target));
            var unrelated = new GameObject("Unrelated binding target");
            try
            {
                var invalid = new[] { new ComponentBindingGenerator.Binding { fieldName = "anchor", component = unrelated.transform } };
                Assert.Throws<ArgumentException>(() => ComponentBindingGenerator.Apply(target, invalid));
                Assert.AreSame(root.transform, type.GetField("anchor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target));
            }
            finally { UnityEngine.Object.DestroyImmediate(unrelated); }
            File.WriteAllText(Path.Combine(directory, "result.json"), "{\"compiled\":true,\"applied\":true,\"validated\":true,\"externalReferenceRejected\":true}");
        }
    }
}
