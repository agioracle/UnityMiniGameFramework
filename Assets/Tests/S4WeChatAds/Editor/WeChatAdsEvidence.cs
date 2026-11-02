using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

// Only the new assembly pair: do not alter the historical evidence listener or its filters.
[InitializeOnLoad]
internal static class WeChatAdsEvidence
{
    static readonly TestRunnerApi Api;
    static WeChatAdsEvidence()
    {
        Api = ScriptableObject.CreateInstance<TestRunnerApi>();
        Api.RegisterCallbacks(new Listener());
    }
    sealed class Listener : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            if (result == null) return;
            var xml = result.ToXml().OuterXml;
            if (!xml.Contains("MiniFramework.WeChatAds.Tests.")) return;
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/S0TestResults"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "s410-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".xml"), xml);
        }
    }
}
