using System;
using System.Collections.Generic;
using System.Linq;
using MiniFramework.Contracts;
using MiniFramework.UI.Standard;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
namespace MiniFramework.UI.Editor
{
    public static class UiTemplateValidator
    {
        public static Result Validate(StandardUiProfile profile,IReadOnlyDictionary<string,string> installed,bool forRelease=false)
        {
            if(profile==null||installed==null)return Fail("ui.profile.missing");
            if(profile.schemaVersion!=1)return Fail("ui.profile.schema");
            if(profile.unity!="2022.3.62f3"||profile.presentation!="uGUI"||profile.animation!="UnityAnimator"||profile.visibilityParameter!=AnimatorPresentation.VisibilityParameter||profile.renderPipeline!="URP-2D"||profile.fontIntegration!="TextMeshPro"||profile.serialization!="JsonUtility-SimpleDTO"||profile.accountMode!="LocalOnly"||profile.assetProvider!="YooAsset"||profile.editorProvider!="FakeOrLocal")return Fail("ui.profile.selection");
            if(profile.uguiVersion!="1.0.0"||profile.urpVersion!="14.0.12"||profile.tmpVersion!="3.0.7"||profile.yooAssetVersion!="3.0.6")return Fail("ui.profile.pin");
            foreach(var pair in RequiredPackages(profile))if(!installed.TryGetValue(pair.Key,out var version)||version!=pair.Value)return Fail("ui.profile.package."+pair.Key);
            // These fields are evidence pointers. Their legal and visual correctness needs human/device validation.
            if(forRelease&&(string.IsNullOrWhiteSpace(profile.fontAssetGuid)||string.IsNullOrWhiteSpace(profile.fontLicenseEvidence)||string.IsNullOrWhiteSpace(profile.glyphCoverageEvidence)))return Fail("ui.profile.font.release_blocked");
            return Result.Success();
        }
        public static IReadOnlyDictionary<string,string> RequiredPackages(StandardUiProfile profile)=>new Dictionary<string,string>{
            {"com.unity.ugui",profile.uguiVersion},{"com.unity.render-pipelines.universal",profile.urpVersion},{"com.unity.textmeshpro",profile.tmpVersion},{"com.tuyoogame.yooasset",profile.yooAssetVersion}};
        public static Result ValidateAssemblyReferences(string name,IEnumerable<string> dependencies)
        {
            foreach(var dep in dependencies)
            {
                if(new[]{"YooAsset","Unity.Addressables","Unity.ResourceManager","DOTween","ZString","FMOD","PrimeTween","MiniGameFramework","Game.Access"}.Any(x=>dep.IndexOf(x,StringComparison.OrdinalIgnoreCase)>=0))return Fail("ui.assembly.optional."+name);
                if(name!="MiniFramework.UI.Editor"&&(dep.IndexOf("UnityEditor",StringComparison.OrdinalIgnoreCase)>=0||dep.IndexOf("EditorFoundation",StringComparison.OrdinalIgnoreCase)>=0||dep.EndsWith(".Editor",StringComparison.Ordinal)))return Fail("ui.assembly.editor."+name);
            }
            return Result.Success();
        }
        [MenuItem("Tools/MiniFramework/Validate UI Template")]
        public static void ValidateCurrent()
        {
            var path=System.IO.Path.Combine(Application.dataPath,"../Build/S3/profile.json");
            var profile=JsonUtility.FromJson<StandardUiProfile>(System.IO.File.ReadAllText(path));
            var packages=UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().ToDictionary(p=>p.name,p=>p.version);
            var result=Validate(profile,packages);if(!result.IsSuccess)throw new InvalidOperationException(result.Error.Code);
            foreach(var assembly in CompilationPipeline.GetAssemblies().Where(a=>a.name=="MiniFramework.UI"||a.name=="MiniFramework.UI.Editor"||a.name=="MiniFramework.UI.Standard"))
            {
                result=ValidateAssemblyReferences(assembly.name,assembly.assemblyReferences.Select(a=>a.name).Concat(assembly.compiledAssemblyReferences.Select(System.IO.Path.GetFileNameWithoutExtension)));
                if(!result.IsSuccess)throw new InvalidOperationException(result.Error.Code);
            }
            Debug.Log("UI template compile profile valid. Production font license/glyph coverage and full G3 remain separate gates.");
        }
        private static Result Fail(string code)=>Result.Failure(new FrameworkError(ErrorKind.InvalidArgument,code));
    }
}
