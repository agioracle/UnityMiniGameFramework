using System;
using System.IO;
using Cysharp.Threading.Tasks;
using MiniFramework.Diagnostics.Tests;
using UnityEditor;
using UnityEngine;
// Opt-in acceptance harness. Commands live in Library; no scene or asset serialization.
[InitializeOnLoad]
internal static class G1PlayAcceptance
{
    private static readonly string Folder=Path.GetFullPath("Library/G1PlayAcceptance");
    private static int entries;
    private static UniTask driver;
    [Serializable] private sealed class Settings {public bool enabled;public int options;}
    [Serializable] private sealed class Evidence {public int entry;public bool passed;public bool domainReloadDisabled;public string error;}
    static G1PlayAcceptance(){EditorApplication.update+=Update;EditorApplication.playModeStateChanged+=Changed;}
    private static void Update()
    {
        var command=Path.Combine(Folder,"command.txt");if(!File.Exists(command)||EditorApplication.isPlayingOrWillChangePlaymode)return;
        var text=File.ReadAllText(command).Trim();File.Delete(command);
        if(text=="configure")
        {
            var saved=Path.Combine(Folder,"original.json");if(!File.Exists(saved))File.WriteAllText(saved,JsonUtility.ToJson(new Settings{enabled=EditorSettings.enterPlayModeOptionsEnabled,options=(int)EditorSettings.enterPlayModeOptions}));
            entries=0;EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
            File.WriteAllText(Path.Combine(Folder,"active"),"1");File.WriteAllText(Path.Combine(Folder,"configured.json"),JsonUtility.ToJson(new Settings{enabled=EditorSettings.enterPlayModeOptionsEnabled,options=(int)EditorSettings.enterPlayModeOptions}));
        }
        else if(text=="restore")
        {
            var original=JsonUtility.FromJson<Settings>(File.ReadAllText(Path.Combine(Folder,"original.json")));
            EditorSettings.enterPlayModeOptionsEnabled=original.enabled;EditorSettings.enterPlayModeOptions=(EnterPlayModeOptions)original.options;
            if(File.Exists(Path.Combine(Folder,"active")))File.Delete(Path.Combine(Folder,"active"));
            File.WriteAllText(Path.Combine(Folder,"restored.json"),JsonUtility.ToJson(new Settings{enabled=EditorSettings.enterPlayModeOptionsEnabled,options=(int)EditorSettings.enterPlayModeOptions}));
        }
    }
    private static void Changed(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode||!File.Exists(Path.Combine(Folder,"active")))return;
        entries++;driver=Run(entries).Preserve();
    }
    private static async UniTask Run(int entry)
    {
        var evidence=new Evidence{entry=entry,domainReloadDisabled=EditorSettings.enterPlayModeOptionsEnabled&&(EditorSettings.enterPlayModeOptions&EnterPlayModeOptions.DisableDomainReload)!=0};
        try{await G1Scenario.RunAsync();evidence.passed=evidence.domainReloadDisabled;}
        catch(Exception error){evidence.error=error.ToString();}
        File.WriteAllText(Path.Combine(Folder,"entry-"+entry+".json"),JsonUtility.ToJson(evidence,true));
    }
}
