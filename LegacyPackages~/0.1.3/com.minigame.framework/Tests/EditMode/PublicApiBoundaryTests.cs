using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using MiniGame.Framework.Editor;
using NUnit.Framework;

namespace MiniGame.Framework.Tests
{
    public sealed class PublicApiBoundaryTests
    {
        public class NestedOwner
        {
            protected class ProtectedContract { public int Value; }
            protected internal class SharedContract { public int Value; }
            private class PrivateContract { public int Value; }
        }

        private static Type BuildFixture(bool protectedField, bool initSetter)
        {
            var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ApiFixture" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
            ModuleBuilder module = assembly.DefineDynamicModule("ApiFixture");
            Type marker = null;
            if (initSetter)
                marker = module.DefineType("System.Runtime.CompilerServices.IsExternalInit", TypeAttributes.Public | TypeAttributes.Sealed).CreateType();
            TypeBuilder type = module.DefineType("ApiBoundary.Fixture", TypeAttributes.Public);
            type.DefineField("Value", typeof(int), protectedField ? FieldAttributes.Family : FieldAttributes.Public);
            PropertyBuilder property = type.DefineProperty("Count", PropertyAttributes.None, typeof(int), Type.EmptyTypes);
            MethodBuilder setter = type.DefineMethod("set_Count", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig);
            setter.SetSignature(typeof(void), marker == null ? null : new[] { marker }, null, new[] { typeof(int) }, null, null);
            setter.GetILGenerator().Emit(OpCodes.Ret);
            property.SetSetMethod(setter);
            return type.CreateType();
        }

        [Test]
        public void ActualFieldVisibilityNarrowingIsReported()
        {
            var before = PublicApiCompatibility.CaptureTypes(new[] { BuildFixture(false, false) });
            var after = PublicApiCompatibility.CaptureTypes(new[] { BuildFixture(true, false) });
            var diff = PublicApiCompatibility.Compare(before, after);
            Assert.IsTrue(diff.requiresCompatibilityReview);
            Assert.IsTrue(diff.removed.Any(entry => entry.Contains("field Value") && entry.Contains("access=public")));
            Assert.IsTrue(diff.added.Any(entry => entry.Contains("field Value") && entry.Contains("access=protected")));
        }

        [Test]
        public void ProtectedNestedTypesAndMembersAreCapturedButPrivateOnesAreNot()
        {
            var captured = PublicApiCompatibility.CaptureTypes(new[] { typeof(NestedOwner) });
            Assert.IsTrue(captured.entries.Any(entry => entry.StartsWith("type ", StringComparison.Ordinal) && entry.Contains("ProtectedContract") && entry.Contains("access=protected")));
            Assert.IsTrue(captured.entries.Any(entry => entry.Contains("ProtectedContract field Value")));
            Assert.IsTrue(captured.entries.Any(entry => entry.Contains("SharedContract") && entry.Contains("access=protected-internal")));
            Assert.IsFalse(captured.entries.Any(entry => entry.Contains("PrivateContract")));
        }

        [Test]
        public void ActualSetterReturnRequiredModifierProducesDifference()
        {
            var before = PublicApiCompatibility.CaptureTypes(new[] { BuildFixture(false, false) });
            var after = PublicApiCompatibility.CaptureTypes(new[] { BuildFixture(false, true) });
            var diff = PublicApiCompatibility.Compare(before, after);
            Assert.IsTrue(diff.requiresCompatibilityReview);
            Assert.IsTrue(diff.added.Any(entry => entry.Contains("method set_Count") && entry.Contains("modreq=[System.Runtime.CompilerServices.IsExternalInit]")));
            Assert.IsTrue(diff.added.Any(entry => entry.Contains("property Count") && entry.Contains("modreq=[System.Runtime.CompilerServices.IsExternalInit]")));
            Assert.IsTrue(diff.removed.Any(entry => entry.Contains("property Count")));
        }

        [Test]
        public void ExpandedMetadataIsStableAndExportsASeparateBaseline()
        {
            var first = PublicApiCompatibility.Capture();
            var second = PublicApiCompatibility.Capture();
            CollectionAssert.AreEqual(first.entries, second.entries);
            Assert.IsFalse(PublicApiCompatibility.Compare(first, second).requiresCompatibilityReview);
            PublicApiCompatibility.ExportBaseline();
        }
    }
}
