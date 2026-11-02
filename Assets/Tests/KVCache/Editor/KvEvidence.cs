using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

[InitializeOnLoad]
internal static class KvEvidence
{
    static readonly TestRunnerApi Api;
    static KvEvidence()
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
            if (!xml.Contains("MiniFramework.KVCache.Tests.")) return;
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/S0TestResults"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "kv-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".xml"), xml);
        }
    }
}
