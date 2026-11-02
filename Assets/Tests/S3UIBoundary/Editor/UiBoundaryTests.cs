using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Runtime;
using MiniFramework.UI;
using MiniFramework.UI.Standard;
using MiniFramework.UI.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Compilation;
using UnityEngine;
using Object=UnityEngine.Object;
namespace MiniFramework.UIBoundary.Tests
{
    public sealed class UiBoundaryTests
    {
        static CleanupContext Cleanup=>new CleanupContext(default,System.Threading.Timeout.InfiniteTimeSpan);
        static Dictionary<string,string> Packages()=>new Dictionary<string,string>{{"com.unity.ugui","1.0.0"},{"com.unity.render-pipelines.universal","14.0.12"},{"com.unity.textmeshpro","3.0.7"},{"com.tuyoogame.yooasset","3.0.6"}};
        readonly List<Object> objects=new List<Object>();readonly List<Scope> scopes=new List<Scope>();
        Scope Owner(){var s=new Scope("view");scopes.Add(s);return s;}
        Animator Animator(bool parameter=true,AnimatorControllerParameterType type=AnimatorControllerParameterType.Bool)
        {
            var go=new GameObject("ui-boundary-test");objects.Add(go);var animator=go.AddComponent<Animator>();
            var controller=new AnimatorController();objects.Add(controller);controller.AddLayer("Base");objects.Add(controller.layers[0].stateMachine);
            if(parameter)controller.AddParameter(AnimatorPresentation.VisibilityParameter,type);
            animator.runtimeAnimatorController=controller;return animator;
        }
        [TearDown] public void TearDown(){foreach(var scope in scopes)Assert.IsTrue(scope.CloseAsync(Cleanup).GetAwaiter().GetResult().IsFullyDrained);foreach(var obj in objects)if(obj!=null)Object.DestroyImmediate(obj);scopes.Clear();objects.Clear();}
        sealed class Pool:IPoolService<GameObject>
        {
            public int Starts;public IScope Owner;public ResourceKey Key;public ResourcePriority Priority;
            public Result<IOwnedOperation<IPoolRent<GameObject>>> Response=Result<IOwnedOperation<IPoolRent<GameObject>>>.Failure(new FrameworkError(ErrorKind.NotReady,"fake.not_ready"));
            public Result<IOwnedOperation<IPoolRent<GameObject>>> Spawn(ResourceKey key,IScope owner,ResourcePriority priority=ResourcePriority.Normal){Starts++;Key=key;Owner=owner;Priority=priority;return Response;}
            public UniTask<Result> PrewarmAsync(ResourceKey key,int count,IScope owner,OperationContext wait)=>throw new NotSupportedException();
            public UniTask<Result> EvictAsync(ResourceKey key,CleanupContext cleanup)=>throw new NotSupportedException();
        }
        static ResourceKey Key=>new ResourceKey("fake","main","v1","ui/home",typeof(GameObject));
        [Test] public void SourceForwardsOwnerKeyAndPriorityWithoutNewLedger(){var pool=new Pool();var owner=Owner();var source=StandardUiProfile.Compose(pool);var result=source.Rent(Key,owner,ResourcePriority.Critical);Assert.AreEqual(1,pool.Starts);Assert.AreSame(owner,pool.Owner);Assert.AreEqual(Key,pool.Key);Assert.AreEqual(ResourcePriority.Critical,pool.Priority);Assert.AreEqual("fake.not_ready",result.Error.Code);}
        [Test] public void SourceRejectsSceneKeysBeforePool(){var pool=new Pool();Assert.IsFalse(new PooledPanelSource(pool).Rent(ResourceKey.Scene("fake","main","v1","ui/home"),Owner()).IsSuccess);Assert.AreEqual(0,pool.Starts);}
        [Test] public void SourceRejectsWrongTypeBeforePool(){var pool=new Pool();Assert.IsFalse(new PooledPanelSource(pool).Rent(new ResourceKey("fake","main","v1","ui/home",typeof(string)),Owner()).IsSuccess);Assert.AreEqual(0,pool.Starts);}
        [Test] public void SourceRequiresInjectedPool(){Assert.Throws<ArgumentNullException>(()=>new PooledPanelSource(null));}
        [Test] public void DefaultProfilePassesWithPinnedPackages(){Assert.IsTrue(UiTemplateValidator.Validate(new StandardUiProfile(),Packages()).IsSuccess);}
        [Test] public void DefaultProfileCannotPassProductionFontGate(){Assert.AreEqual("ui.profile.font.release_blocked",UiTemplateValidator.Validate(new StandardUiProfile(),Packages(),true).Error.Code);}
        [Test] public void MissingSelectedPackageFails(){var packages=Packages();packages.Remove("com.unity.textmeshpro");Assert.AreEqual("ui.profile.package.com.unity.textmeshpro",UiTemplateValidator.Validate(new StandardUiProfile(),packages).Error.Code);}
        [Test] public void DriftedInstalledPackageFails(){var packages=Packages();packages["com.unity.render-pipelines.universal"]="15.0.0";Assert.IsFalse(UiTemplateValidator.Validate(new StandardUiProfile(),packages).IsSuccess);}
        [Test] public void ProfileCannotSilentlyChangeItsPinnedVersion(){var profile=new StandardUiProfile{tmpVersion="3.1.0"};Assert.AreEqual("ui.profile.pin",UiTemplateValidator.Validate(profile,Packages()).Error.Code);}
        [Test] public void UnsupportedSchemaFails(){Assert.AreEqual("ui.profile.schema",UiTemplateValidator.Validate(new StandardUiProfile{schemaVersion=2},Packages()).Error.Code);}
        [Test] public void OptionalAnimationCannotReplaceDefaultSilently(){Assert.AreEqual("ui.profile.selection",UiTemplateValidator.Validate(new StandardUiProfile{animation="DOTween"},Packages()).Error.Code);}
        [Test] public void NullProfileIsRejected(){Assert.IsFalse(UiTemplateValidator.Validate(null,Packages()).IsSuccess);}
        [Test] public void CheckedInProfileRoundTripsWithSimpleDtoSerializer(){var profile=JsonUtility.FromJson<StandardUiProfile>(File.ReadAllText(Path.Combine(Application.dataPath,"../Build/S3/profile.json")));var copy=JsonUtility.FromJson<StandardUiProfile>(JsonUtility.ToJson(profile));Assert.IsTrue(UiTemplateValidator.Validate(copy,Packages()).IsSuccess);Assert.AreEqual("UnityAnimator",copy.animation);Assert.AreEqual("",copy.fontAssetGuid);}
        [Test] public void AllOptionalAssemblyDependenciesAreRejected(){foreach(var dep in new[]{"YooAsset","Unity.Addressables","Unity.ResourceManager","DOTween.Modules","ZString","FMODUnity","PrimeTween","MiniGameFramework.Core.Editor"})Assert.IsFalse(UiTemplateValidator.ValidateAssemblyReferences("MiniFramework.UI",new[]{dep}).IsSuccess,dep);}
        [Test] public void RuntimeCannotReferenceEditorFoundation(){Assert.IsFalse(UiTemplateValidator.ValidateAssemblyReferences("MiniFramework.UI",new[]{"MiniFramework.EditorFoundation"}).IsSuccess);}
        [Test] public void DotNetResourceManagerIsNotAnAddressablesDependency(){Assert.IsTrue(UiTemplateValidator.ValidateAssemblyReferences("MiniFramework.UI",new[]{"System.Resources.ResourceManager"}).IsSuccess);}
        [Test] public void EditorMayReferenceMinimalFoundation(){Assert.IsTrue(UiTemplateValidator.ValidateAssemblyReferences("MiniFramework.UI.Editor",new[]{"MiniFramework.EditorFoundation","MiniFramework.UI"}).IsSuccess);}
        [Test] public void RuntimeAndStandardAreInActualPlayerCompilation(){var assemblies=CompilationPipeline.GetAssemblies(AssembliesType.Player);foreach(var name in new[]{"MiniFramework.UI","MiniFramework.UI.Standard"}){var assembly=assemblies.Single(a=>a.name==name);Assert.IsTrue(UiTemplateValidator.ValidateAssemblyReferences(name,assembly.assemblyReferences.Select(a=>a.name).Concat(assembly.compiledAssemblyReferences.Select(Path.GetFileNameWithoutExtension))).IsSuccess,string.Join(";",assembly.assemblyReferences.Select(a=>a.name).Concat(assembly.compiledAssemblyReferences.Select(Path.GetFileNameWithoutExtension))));}Assert.IsFalse(assemblies.Any(a=>a.name=="MiniFramework.UI.Editor"));}
        [Test] public void ActualEditorAssemblyHasOnlyDeclaredLocalDependencies(){var assembly=CompilationPipeline.GetAssemblies().Single(a=>a.name=="MiniFramework.UI.Editor");CollectionAssert.AreEquivalent(new[]{"MiniFramework.UI","MiniFramework.UI.Standard","MiniFramework.EditorFoundation","MiniFramework.Contracts"},assembly.assemblyReferences.Select(a=>a.name).Where(n=>n.StartsWith("MiniFramework.")));Assert.IsTrue(UiTemplateValidator.ValidateAssemblyReferences(assembly.name,assembly.assemblyReferences.Select(a=>a.name)).IsSuccess);}
        [Test] public void AnimatorRequiresAuthoredController(){var go=new GameObject("missing-controller");objects.Add(go);Assert.AreEqual("ui.animation.controller",AnimatorPresentation.Create(go.AddComponent<Animator>(),Owner(),"anim").Error.Code);}
        [Test] public void AnimatorRequiresVisibleBool(){Assert.AreEqual("ui.animation.parameter",AnimatorPresentation.Create(Animator(false),Owner(),"anim").Error.Code);}
        [Test] public void WrongAnimatorParameterTypeIsRejected(){Assert.AreEqual("ui.animation.parameter",AnimatorPresentation.Create(Animator(true,AnimatorControllerParameterType.Float),Owner(),"anim").Error.Code);}
        [Test] public void ClosedOwnerCannotCreatePresentation(){var owner=Owner();owner.CloseAsync(Cleanup).GetAwaiter().GetResult();Assert.IsFalse(AnimatorPresentation.Create(Animator(),owner,"anim").IsSuccess);}
        [Test] public void ClosingOwnerDisablesAnimatorAndRejectsLateWrites(){var animator=Animator();var owner=Owner();var created=AnimatorPresentation.Create(animator,owner,"anim");Assert.IsTrue(created.IsSuccess);Assert.IsTrue(owner.CloseAsync(Cleanup).GetAwaiter().GetResult().IsFullyDrained);Assert.IsFalse(animator.enabled);Assert.AreEqual(ErrorKind.InvalidState,created.Value.SetVisible(true).Error.Kind);}
        [Test] public void DefaultAnimatorUsesUnscaledTimeAndNoRootMotion(){var animator=Animator();var driver=AnimatorPresentation.Create(animator,Owner(),"anim").Value;Assert.IsTrue(driver.SetVisible(true).IsSuccess);Assert.AreEqual(AnimatorUpdateMode.UnscaledTime,animator.updateMode);Assert.IsFalse(animator.applyRootMotion);Assert.IsTrue(animator.GetBool(AnimatorPresentation.VisibilityParameter));Assert.IsTrue(driver.SetVisible(false).IsSuccess);Assert.IsFalse(animator.GetBool(AnimatorPresentation.VisibilityParameter));}
        [Test] public void InactiveTargetDoesNotAcceptVisibilityCommands(){var animator=Animator();var driver=AnimatorPresentation.Create(animator,Owner(),"anim").Value;animator.gameObject.SetActive(false);Assert.AreEqual(ErrorKind.Unavailable,driver.SetVisible(true).Error.Kind);}
        [Test] public void ReplacedControllerRejectsStalePresentation(){var animator=Animator();var driver=AnimatorPresentation.Create(animator,Owner(),"anim").Value;animator.runtimeAnimatorController=null;Assert.AreEqual("ui.animation.controller.changed",driver.SetVisible(true).Error.Code);}
        [Test] public void DestroyedTargetStillDrains(){var animator=Animator();var owner=Owner();var driver=AnimatorPresentation.Create(animator,owner,"anim").Value;Object.DestroyImmediate(animator.gameObject);Assert.AreEqual(ErrorKind.Unavailable,driver.SetVisible(true).Error.Kind);Assert.IsTrue(owner.CloseAsync(Cleanup).GetAwaiter().GetResult().IsFullyDrained);}
    }
}
