using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

[InitializeOnLoad]
internal static class S407ContentHttpEvidence
{
    static readonly TestRunnerApi Api;
    static S407ContentHttpEvidence() { Api = ScriptableObject.CreateInstance<TestRunnerApi>(); Api.RegisterCallbacks(new Listener()); }
    sealed class Listener : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
        public void RunFinished(ITestResultAdaptor result)
        {
            if (result == null) return;
            var xml = result.ToXml().OuterXml;
            if (!xml.Contains("MiniFramework.S407ContentHttp.Tests.PlayMode")) return;
            Directory.CreateDirectory("Library/S0TestResults");
            File.WriteAllText("Library/S0TestResults/s407content-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + ".xml", xml);
        }
    }
}
