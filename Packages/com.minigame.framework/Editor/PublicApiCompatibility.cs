using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using MiniGame.Framework.Reactive;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Framework.Editor
{
    public static class PublicApiCompatibility
    {
        [Serializable]
        public sealed class Snapshot
        {
            public int schemaVersion = 2;
            public string unityVersion;
            public string[] entries;
            public string scope = "Public/protected declared API metadata, defaults, generic constraints and selected compiler attributes. Does not prove behavioral or binary compatibility.";
        }
        [Serializable]
        public sealed class Difference
        {
            public string[] removed;
            public string[] added;
            public bool requiresCompatibilityReview;
            public string scope = "Removed signatures are potential breaking changes; added overloads and types still require review. No behavior, save migration or platform compatibility verdict.";
        }
        private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        public static Snapshot Capture()
        {
            var types = new List<Type>();
            foreach (Assembly assembly in new[] { typeof(TimerService).Assembly, typeof(ResourceService).Assembly, typeof(DisplayLifetime).Assembly })
                types.AddRange(assembly.GetExportedTypes());
            return CaptureTypes(types.ToArray());
        }

        public static Snapshot CaptureTypes(Type[] types)
        {
            if (types == null) throw new ArgumentNullException(nameof(types));
            var entries = new SortedSet<string>(StringComparer.Ordinal);
            var reachable = new HashSet<Type>();
            foreach (Type root in types)
            {
                if (root == null) throw new ArgumentException("Every type must be valid.", nameof(types));
                CollectTypes(root, reachable);
            }
            foreach (Type type in reachable)
            {
                string name = TypeName(type);
                var interfaces = new List<string>();
                foreach (Type item in type.GetInterfaces()) interfaces.Add(TypeName(item));
                interfaces.Sort(StringComparer.Ordinal);
                entries.Add("type " + name + " kind=" + (type.IsEnum ? "enum" : type.IsInterface ? "interface" : type.IsValueType ? "value" : "class") +
                    " access=" + TypeAccess(type) + " abstract=" + type.IsAbstract + " sealed=" + type.IsSealed + " base=" + TypeName(type.BaseType) + " interfaces=" + string.Join(",", interfaces) + Constraints(type.GetGenericArguments()) + Attributes(type));
                foreach (ConstructorInfo member in type.GetConstructors(Declared))
                    if (Visible(member)) entries.Add(name + " ctor " + MethodFlags(member) + Parameters(member.GetParameters()) + Attributes(member));
                foreach (MethodInfo member in type.GetMethods(Declared))
                    if (Visible(member)) entries.Add(name + " method " + member.Name + " " + MethodFlags(member) + " returns=" + TypeName(member.ReturnType) +
                        " return" + Modifiers(member.ReturnParameter) + Parameters(member.GetParameters()) + Constraints(member.GetGenericArguments()) + Attributes(member) + Attributes(member.ReturnParameter));
                foreach (PropertyInfo member in type.GetProperties(Declared))
                {
                    MethodInfo get = member.GetGetMethod(true), set = member.GetSetMethod(true);
                    if ((get != null && Visible(get)) || (set != null && Visible(set)))
                        entries.Add(name + " property " + member.Name + " type=" + TypeName(member.PropertyType) + Parameters(member.GetIndexParameters()) +
                            " get=" + Accessor(get) + " set=" + Accessor(set) + Attributes(member));
                }
                foreach (FieldInfo member in type.GetFields(Declared))
                    if (member.IsPublic || member.IsFamily || member.IsFamilyOrAssembly)
                        entries.Add(name + " field " + member.Name + " type=" + TypeName(member.FieldType) + " access=" + (member.IsPublic ? "public" : member.IsFamilyOrAssembly ? "protected-internal" : "protected") + " static=" + member.IsStatic +
                            " readonly=" + member.IsInitOnly + " const=" + member.IsLiteral + (member.IsLiteral ? " value=" + Value(member.GetRawConstantValue()) : "") + Attributes(member));
                foreach (EventInfo member in type.GetEvents(Declared))
                {
                    MethodInfo add = member.GetAddMethod(true), remove = member.GetRemoveMethod(true);
                    if ((add != null && Visible(add)) || (remove != null && Visible(remove)))
                        entries.Add(name + " event " + member.Name + " type=" + TypeName(member.EventHandlerType) + " add=" + Accessor(add) + " remove=" + Accessor(remove) + Attributes(member));
                }
            }
            var result = new string[entries.Count];
            entries.CopyTo(result);
            return new Snapshot { unityVersion = Application.unityVersion, entries = result };
        }

        public static Difference Compare(Snapshot baseline, Snapshot current)
        {
            Validate(baseline); Validate(current);
            var before = new SortedSet<string>(baseline.entries, StringComparer.Ordinal);
            var after = new SortedSet<string>(current.entries, StringComparer.Ordinal);
            var removed = new SortedSet<string>(before, StringComparer.Ordinal);
            var added = new SortedSet<string>(after, StringComparer.Ordinal);
            removed.ExceptWith(after); added.ExceptWith(before);
            return new Difference { removed = Copy(removed), added = Copy(added), requiresCompatibilityReview = removed.Count != 0 || added.Count != 0 };
        }

        [MenuItem("MiniGame/Framework/Export Detailed API Baseline")]
        public static void ExportBaseline() => WriteReport("framework-api-v2-", JsonUtility.ToJson(Capture(), true));

        [MenuItem("MiniGame/Framework/Compare Detailed API Baseline")]
        public static void CompareBaseline()
        {
            string path = EditorUtility.OpenFilePanel("Select an explicit v2 API baseline from Builds", Path.GetFullPath("Builds/Reports"), "json");
            if (string.IsNullOrEmpty(path)) return;
            BuildPathGuard.RequireChild(Path.GetFullPath("Builds"), path);
            if (new FileInfo(path).Length > 1048576) throw new ArgumentException("Baseline file exceeds 1 MiB.");
            Snapshot baseline = JsonUtility.FromJson<Snapshot>(File.ReadAllText(path));
            WriteReport("framework-api-diff-", JsonUtility.ToJson(Compare(baseline, Capture()), true));
        }

        private static void WriteReport(string prefix, string json)
        {
            string path = "Builds/Reports/" + prefix + Guid.NewGuid().ToString("N") + ".json";
            BuildPathGuard.RequireChild(Path.GetFullPath("Builds"), Path.GetFullPath(path));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream)) writer.Write(json);
            Debug.Log("[MiniGame] API metadata report written: " + path);
        }
        private static void Validate(Snapshot snapshot)
        {
            if (snapshot == null || snapshot.schemaVersion != 2 || snapshot.entries == null || snapshot.entries.Length == 0 || snapshot.entries.Length > 20000)
                throw new ArgumentException("An explicit nonempty v2 API baseline is required; legacy inventories cannot be used as equivalent baselines.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string entry in snapshot.entries)
                if (string.IsNullOrWhiteSpace(entry) || entry.Length > 16384 || !unique.Add(entry)) throw new ArgumentException("Invalid or duplicate API entries.");
        }
        private static string[] Copy(SortedSet<string> values) { var result = new string[values.Count]; values.CopyTo(result); return result; }
        private static bool Visible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
        private static void CollectTypes(Type type, HashSet<Type> types)
        {
            if (!types.Add(type)) return;
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                if (nested.IsNestedPublic || nested.IsNestedFamily || nested.IsNestedFamORAssem) CollectTypes(nested, types);
        }
        private static string TypeAccess(Type type) => type.IsNestedPublic || type.IsPublic ? "public" :
            type.IsNestedFamORAssem ? "protected-internal" : type.IsNestedFamily ? "protected" : "nonpublic";
        private static string Modifiers(ParameterInfo parameter)
        {
            var required = new List<string>();
            var optional = new List<string>();
            foreach (Type modifier in parameter.GetRequiredCustomModifiers()) required.Add(TypeName(modifier));
            foreach (Type modifier in parameter.GetOptionalCustomModifiers()) optional.Add(TypeName(modifier));
            return " modreq=[" + string.Join(",", required) + "] modopt=[" + string.Join(",", optional) + "]";
        }
        private static string Accessor(MethodInfo method) => method == null ? "none" : MethodFlags(method) + " return" + Modifiers(method.ReturnParameter);
        private static string MethodFlags(MethodBase method) =>
            (method.IsPublic ? "public" : method.IsFamilyOrAssembly ? "protected-internal" : method.IsFamily ? "protected" : "nonpublic") +
            " static=" + method.IsStatic + " abstract=" + method.IsAbstract + " virtual=" + method.IsVirtual + " final=" + method.IsFinal;
        private static string TypeName(Type type)
        {
            if (type == null) return "none";
            if (type.IsByRef) return TypeName(type.GetElementType()) + "&";
            if (type.IsPointer) return TypeName(type.GetElementType()) + "*";
            if (type.IsArray) return TypeName(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            if (type.IsGenericParameter) return (type.DeclaringMethod == null ? "!" : "!!") + type.GenericParameterPosition + ":" + type.Name;
            if (!type.IsGenericType) return type.FullName ?? type.Name;
            var arguments = new List<string>();
            foreach (Type argument in type.GetGenericArguments()) arguments.Add(TypeName(argument));
            return type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", arguments) + ">";
        }
        private static string Parameters(ParameterInfo[] parameters)
        {
            var result = new List<string>();
            foreach (ParameterInfo parameter in parameters)
            {
                result.Add(parameter.Name + ":" + TypeName(parameter.ParameterType) + " in=" + parameter.IsIn + " out=" + parameter.IsOut +
                    " optional=" + parameter.IsOptional + " default=" + (parameter.HasDefaultValue ? Value(parameter.DefaultValue) : "absent") +
                    Modifiers(parameter) + Attributes(parameter));
            }
            return " parameters=[" + string.Join(";", result) + "]";
        }
        private static string Constraints(Type[] arguments)
        {
            var result = new List<string>();
            foreach (Type argument in arguments)
            {
                if (!argument.IsGenericParameter) continue;
                var constraints = new List<string>();
                foreach (Type constraint in argument.GetGenericParameterConstraints()) constraints.Add(TypeName(constraint));
                constraints.Sort(StringComparer.Ordinal);
                result.Add(TypeName(argument) + " flags=" + (int)argument.GenericParameterAttributes + " types=" + string.Join(",", constraints));
            }
            return " generics=[" + string.Join(";", result) + "]";
        }
        private static string Attributes(ICustomAttributeProvider provider)
        {
            IList<CustomAttributeData> attributes;
            if (provider is MemberInfo member) attributes = CustomAttributeData.GetCustomAttributes(member);
            else if (provider is ParameterInfo parameter) attributes = CustomAttributeData.GetCustomAttributes(parameter);
            else return "";
            var result = new List<string>();
            foreach (CustomAttributeData attribute in attributes)
            {
                string name = attribute.AttributeType.FullName;
                if (name != "System.ObsoleteAttribute" && name != "System.ParamArrayAttribute" && name != "System.Runtime.CompilerServices.IsReadOnlyAttribute" && name != "System.Runtime.CompilerServices.ExtensionAttribute") continue;
                var values = new List<string>();
                foreach (CustomAttributeTypedArgument value in attribute.ConstructorArguments) values.Add(TypeName(value.ArgumentType) + "=" + Value(value.Value));
                result.Add(name + "(" + string.Join(",", values) + ")");
            }
            result.Sort(StringComparer.Ordinal);
            return " attributes=[" + string.Join(";", result) + "]";
        }
        private static string Value(object value)
        {
            if (value == null) return "null";
            if (value == DBNull.Value) return "dbnull";
            if (value == Missing.Value) return "missing";
            if (value is string text) return "string:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));
            if (value is char character) return "char:" + (int)character;
            return TypeName(value.GetType()) + ":" + Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
