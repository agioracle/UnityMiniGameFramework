using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
namespace MiniFramework.Config.Editor
{
    public static class ConfigGeneration
    {
        // Override only when python3 is installed elsewhere. Never invoke a shell or download during a build.
        public static string PythonExecutable=>EditorPrefs.GetString("MiniFramework.Config.Python",Application.platform==RuntimePlatform.WindowsEditor?"python":"/usr/bin/python3");
        public static void Run(bool checkOnly)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("config.editor.busy");
            var root=Directory.GetParent(Application.dataPath).Parent.FullName;
            var script=Path.Combine(root,"Tools/S3/generate_config.py");
            var start=new ProcessStartInfo(PythonExecutable,"\""+script.Replace("\"","\\\"")+"\""+(checkOnly?" --check":"")){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=Process.Start(start))
            {
                var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
                if(!process.WaitForExit(90000)){process.Kill();throw new InvalidOperationException("config.generate.timeout");}
                var message=output.GetAwaiter().GetResult()+error.GetAwaiter().GetResult();
                if(process.ExitCode!=0)throw new InvalidOperationException("config.generate.failed: "+message);
            }
            if(!checkOnly)AssetDatabase.Refresh();
        }
        [MenuItem("Tools/MiniFramework/Config/Generate")] static void Generate(){Run(false);}
        [MenuItem("Tools/MiniFramework/Config/Validate Generated")] static void Validate(){Run(true);}
    }
    public sealed class ConfigBuildCheck:IPreprocessBuildWithReport
    {
        public int callbackOrder=>-10;
        public void OnPreprocessBuild(BuildReport report){try{ConfigGeneration.Run(true);}catch(Exception error){throw new BuildFailedException(error.Message);}}
    }
}
