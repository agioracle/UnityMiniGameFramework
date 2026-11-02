using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

// Dedicated recorder survives PlayMode domain reload; historical recorder/filter is untouched.
[InitializeOnLoad]
internal static class S407MemoryEvidence
{
    static readonly TestRunnerApi Api;
    static S407MemoryEvidence()
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
            if (!xml.Contains("MiniFramework.S407Memory.Tests.") && !xml.Contains("MiniFramework.WeChat.Tests.") && !xml.Contains("MiniFramework.Platform.Tests.") && !xml.Contains("MiniFramework.Maintenance.Tests.")) return;
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/S0TestResults"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "s407memory-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".xml"), xml);
        }
    }
}
