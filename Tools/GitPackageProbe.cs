using System;
using System.IO;
using System.Linq;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEditor.PackageManager.UI;
using UnityEngine;

public static class GitPackageProbe
{
    public static void VerifyAndImport()
    {
        if (!Application.isBatchMode || !File.Exists("git-install-validation.json"))
            throw new InvalidOperationException("Explicit Git package validation project required.");
        PackageInfo package = PackageInfo.FindForPackageName("com.minigame.framework");
        if (package == null || package.version != "0.2.0") throw new InvalidOperationException("Unexpected installed package version.");
        foreach (string name in new[] { "com.cysharp.unitask", "com.cysharp.r3", "com.minigame.dependencies.r3", "com.tuyoogame.yooasset" })
            if (PackageInfo.FindForPackageName(name) != null) throw new InvalidOperationException("Duplicate external dependency: " + name);
        var samples = Sample.FindByPackage(package.name, package.version).ToArray();
        if (samples.Length != 1) throw new InvalidOperationException("Expected exactly one importable sample.");
        Sample sample = samples[0];
        if (Directory.Exists(sample.importPath)) throw new IOException("Never overwrite an existing imported sample.");
        if (!sample.Import((Sample.ImportOptions)0)) throw new InvalidOperationException("Package Manager sample import failed.");
        Directory.CreateDirectory("Builds");
        File.WriteAllText("Builds/git-import.json", JsonUtility.ToJson(new Receipt
        {
            name = package.name, version = package.version, source = package.source.ToString(),
            resolvedPath = package.resolvedPath, sample = sample.importPath, unity = Application.unityVersion
        }, true));
        AssetDatabase.Refresh();
    }
    [Serializable]
    private sealed class Receipt
    {
        public string name; public string version; public string source;
        public string resolvedPath; public string sample; public string unity;
    }
}
