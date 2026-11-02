using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

// Test-only listener: re-register after domain reload so PlayMode evidence survives MCP callbacks.
[InitializeOnLoad]
internal static class S0TestEvidence
{
    private static readonly TestRunnerApi Api;
    static S0TestEvidence()
    {
        Api = ScriptableObject.CreateInstance<TestRunnerApi>();
        Api.RegisterCallbacks(new Listener());
    }
    private sealed class Listener : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test)
        {
            if(test!=null&&test.FullName.StartsWith("Revive",StringComparison.Ordinal))
                File.AppendAllText("Library/S406TestProgress.log",DateTime.UtcNow.ToString("O")+" START "+test.FullName+"\n");
        }
        public void TestFinished(ITestResultAdaptor result)
        {
            if(result!=null&&result.Test.FullName.StartsWith("Revive",StringComparison.Ordinal))
            {
                File.AppendAllText("Library/S406TestProgress.log",DateTime.UtcNow.ToString("O")+" END "+result.Test.FullName+"\n");
                Directory.CreateDirectory("Library/S406CaseResults");
                File.WriteAllText("Library/S406CaseResults/"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")+".xml",result.ToXml().OuterXml);
            }
        }
        public void RunFinished(ITestResultAdaptor result)
        {
            if (result == null) return;
            var xml = result.ToXml().OuterXml;
            if (!xml.Contains("MiniFramework.WeChatStorage.Tests.") && !xml.Contains("MiniFramework.WeChat.Tests.") && !xml.Contains("Game.Revive.Tests.") && !xml.Contains("MiniFramework.Ownership.Tests.") && !xml.Contains("MiniFramework.Ads.Tests.") && !xml.Contains("MiniFramework.Platform.Tests.") && !xml.Contains("MiniFramework.Http.Tests.") && !xml.Contains("MiniFramework.StorageRecovery.Tests.") && !xml.Contains("MiniFramework.Storage.Tests.") && !xml.Contains("MiniFramework.Template.") && !xml.Contains("MiniFramework.InputAudio.Tests.") && !xml.Contains("MiniFramework.Scenes.Tests.") && !xml.Contains("MiniFramework.SceneSetup.Tests.") && !xml.Contains("MiniFramework.SceneCleanup.Tests.") && !xml.Contains("MiniFramework.Config.Tests.") && !xml.Contains("MiniFramework.Config.Editor.Tests.") && !xml.Contains("MiniFramework.UI.Tests.") && !xml.Contains("MiniFramework.UIBindings.Tests.") && !xml.Contains("MiniFramework.UIBoundary.Tests.") && !xml.Contains("MiniFramework.Package.Tests.") && !xml.Contains("MiniFramework.Tests.") && !xml.Contains("MiniFramework.Contracts.Tests.") && !xml.Contains("MiniFramework.Context.Tests.") && !xml.Contains("MiniFramework.Scope.Tests.") && !xml.Contains("MiniFramework.Primitives.Tests.") && !xml.Contains("MiniFramework.Host.Tests.") && !xml.Contains("MiniFramework.Diagnostics.Tests.") && !xml.Contains("MiniFramework.Resources.Tests.") && !xml.Contains("MiniFramework.Assets.Tests.") && !xml.Contains("MiniFramework.Providers.Tests.") && !xml.Contains("MiniFramework.Maintenance.Tests.") && !xml.Contains("MiniFramework.Pool.Tests.") && !xml.Contains("MiniFramework.ResourceScheduler.Tests.")) return;
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/S0TestResults"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".xml");
            File.WriteAllText(path, xml);
        }
    }
}
