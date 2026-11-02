using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
[InitializeOnLoad]
internal static class S407HostLifecycleEvidence
{
    static readonly TestRunnerApi Api;
    static S407HostLifecycleEvidence(){Api=ScriptableObject.CreateInstance<TestRunnerApi>();Api.RegisterCallbacks(new Listener());}
    sealed class Listener:ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun){} public void TestStarted(ITestAdaptor test){} public void TestFinished(ITestResultAdaptor result){}
        public void RunFinished(ITestResultAdaptor result)
        {
            if(result==null)return;var xml=result.ToXml().OuterXml;
            if(!xml.Contains("MiniFramework.S407HostLifecycle.Tests."))return;
            var dir=Path.GetFullPath(Path.Combine(Application.dataPath,"../Library/S0TestResults"));Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir,"s407hostlife-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")+".xml"),xml);
        }
    }
}
