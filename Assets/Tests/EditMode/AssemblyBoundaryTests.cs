using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;

public sealed class AssemblyBoundaryTests
{
    [Test] public void GameAccessOnlyReferencesContractsAndAsyncPrimitives()
    {
        var assembly = CompilationPipeline.GetAssemblies().Single(a => a.name == "Game.Access");
        CollectionAssert.AreEquivalent(new[] { "MiniFramework.Contracts", "UniTask" }, assembly.assemblyReferences.Select(a => a.name).ToArray());
    }
    [Test] public void KernelHasNoEngineOrEditorDependency()
    {
        var assembly = CompilationPipeline.GetAssemblies(AssembliesType.Player).Single(a => a.name == "MiniFramework.Kernel");
        var forbidden = assembly.compiledAssemblyReferences.Where(p => Path.GetFileName(p).StartsWith("UnityEngine") || Path.GetFileName(p).StartsWith("UnityEditor")).ToArray();
        CollectionAssert.IsEmpty(forbidden, string.Join("\n", forbidden));
        var emitted = System.Reflection.Assembly.Load("MiniFramework.Kernel").GetReferencedAssemblies();
        Assert.IsFalse(emitted.Any(a => a.Name.StartsWith("UnityEngine") || a.Name.StartsWith("UnityEditor")));
    }
    [Test] public void RuntimeHasNoOptionalSdkOrEditorDependency()
    {
        var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player).Where(a => (a.name.StartsWith("MiniFramework.") || a.name.StartsWith("Game.")) && !a.name.Contains(".Tests."));
        Assert.GreaterOrEqual(assemblies.Count(), 7);
        Assert.IsFalse(assemblies.Any(a => a.name == "MiniFramework.Providers.Local.Editor"), "Editor local backend must not enter Player.");
        foreach (var assembly in assemblies)
        foreach (var dep in assembly.assemblyReferences.Select(a => a.name).Concat(assembly.compiledAssemblyReferences.Select(Path.GetFileName)))
        {
            // Only the isolated SDK adapter may reference the pinned SDK assembly.
            if (assembly.name == "MiniFramework.Providers.YooAsset" && dep == "YooAsset") continue;
            Assert.IsFalse(new[] { "Addressables", "FMOD", "YooAsset", "WeChat", "Douyin", "UnityEditor", "EditorFoundation" }.Any(x => dep.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0), assembly.name + " -> " + dep);
        }
    }
    [Test] public void MissingInjectedBackendFailsAtComposition()
    {
        Assert.Throws<ArgumentNullException>(() => new Game.Boot.LocalGame(null));
    }
}
