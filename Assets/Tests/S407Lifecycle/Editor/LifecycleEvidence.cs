using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

// Dedicated recorder survives PlayMode domain reload; historical recorder/filter is untouched.
[InitializeOnLoad]
internal static class S407LifecycleEvidence
{
    static readonly TestRunnerApi Api;
    static S407LifecycleEvidence()
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
            if (!xml.Contains("MiniFramework.S407Lifecycle.Tests.PlayMode")) return;
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/S0TestResults"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "s407life-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".xml"), xml);
        }
    }
}
