using System;
using System.Collections.Generic;
using UnityEngine;
namespace Game.Boot
{
    public static class SampleContent
    {
        public const string Provider="sample-resources",Package="main",Version="v1",Root="MiniFrameworkSample/";
        public const string AssetRoot="Assets/MiniFrameworkSample";
        public const string BootstrapScene=AssetRoot+"/Scenes/LocalBootstrap.unity";
        public const string TutorialScene=AssetRoot+"/Scenes/Tutorial.unity";
        public const string SecondScene=AssetRoot+"/Scenes/Second.unity";
        public static readonly string[] FirstPlay={"config/catalog","config/tblevel","config/tbreward","ui/home","ui/level","audio/click","levels/tutorial","levels/second"};
        public static readonly IReadOnlyDictionary<string,Type> Types=new Dictionary<string,Type>(StringComparer.Ordinal){
            {"config/catalog",typeof(TextAsset)},{"config/tblevel",typeof(TextAsset)},{"config/tbreward",typeof(TextAsset)},
            {"ui/home",typeof(GameObject)},{"ui/level",typeof(GameObject)},{"audio/click",typeof(AudioClip)}};
        public static readonly IReadOnlyDictionary<string,string> Scenes=new Dictionary<string,string>(StringComparer.Ordinal){
            {"levels/tutorial",TutorialScene},{"levels/second",SecondScene}};
    }
    [Serializable] public sealed class SampleCatalog
    {
        public int format;public string package,version,configVersion,schema;public SampleCatalogFile[] files;
    }
    [Serializable] public sealed class SampleCatalogFile {public string address,path,sha256;}
}
