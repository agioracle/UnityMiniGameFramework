using System;
using System.IO;
using System.Linq;
using MiniFramework.Config.Editor;
using NUnit.Framework;
using UnityEditor;
namespace MiniFramework.Config.Editor.Tests
{
    public sealed class ConfigEditorTests
    {
        [Test] public void PinnedLubanCheckAcceptsCurrentGeneratedOutputs(){Assert.DoesNotThrow(()=>ConfigGeneration.Run(true));}
        [Test] public void BuildCallbackValidatesGeneratedConfiguration(){Assert.DoesNotThrow(()=>new ConfigBuildCheck().OnPreprocessBuild(null));}
        [Test] public void GeneratedCodeBelongsToConsumerAssembly(){Assert.AreEqual("Game.Config",typeof(Game.Config.Generated.Tables).Assembly.GetName().Name);Assert.IsFalse(typeof(Game.Config.Generated.Tables).Assembly.GetReferencedAssemblies().Any(a=>a.Name.StartsWith("MiniGameFramework")||a.Name.EndsWith(".Editor")||a.Name=="YooAsset"));}
        [Test] public void ReadOnlyCheckPreservesGeneratedFileTimestamp(){var path="Assets/Game/Config/Generated/Tables.cs";var before=File.GetLastWriteTimeUtc(path);ConfigGeneration.Run(true);Assert.AreEqual(before,File.GetLastWriteTimeUtc(path));}
    }
}
