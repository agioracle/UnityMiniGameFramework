using System;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using UnityEngine;
namespace MiniFramework.UI
{
    // Synchronous presentation command, NOT proof that an open/close animation finished.
    // One dedicated Animator per presentation owner. Register cleanup before enabling it.
    public sealed class AnimatorPresentation : IAsyncCleanup
    {
        public const string VisibilityParameter="Visible";
        private readonly Animator animator;
        private readonly RuntimeAnimatorController controller;
        private readonly int parameter;
        private bool stopped;private int generation;
        public string Id {get;}
        private AnimatorPresentation(Animator animator,string id){this.animator=animator;controller=animator.runtimeAnimatorController;Id=id;parameter=Animator.StringToHash(VisibilityParameter);}
        public static Result<AnimatorPresentation> Create(Animator animator,IScope owner,string id)
        {
            if(!PlayerLoopHelper.IsMainThread)return Reject("ui.animation.thread",ErrorKind.InvalidState);
            if(animator==null||owner==null||string.IsNullOrWhiteSpace(id))return Reject("ui.animation.argument",ErrorKind.InvalidArgument);
            if(owner.State!=ScopeState.Open)return Reject("ui.animation.owner",ErrorKind.InvalidState);
            if(animator.runtimeAnimatorController==null)return Reject("ui.animation.controller",ErrorKind.InvalidArgument);
            bool found=false;foreach(var value in animator.parameters)if(value.name==VisibilityParameter&&value.type==AnimatorControllerParameterType.Bool){found=true;break;}
            if(!found)return Reject("ui.animation.parameter",ErrorKind.InvalidArgument);
            var driver=new AnimatorPresentation(animator,id);var registered=owner.Cleanup.Register(driver);
            return registered.IsSuccess?Result<AnimatorPresentation>.Success(driver):Result<AnimatorPresentation>.Failure(registered.Error);
        }
        public Result SetVisible(bool visible)
        {
            if(!PlayerLoopHelper.IsMainThread)return Fail("ui.animation.thread",ErrorKind.InvalidState);
            if(stopped)return Fail("ui.animation.closed",ErrorKind.InvalidState);
            if(animator==null||!animator.gameObject.activeInHierarchy)return Fail("ui.animation.target",ErrorKind.Unavailable);
            if(animator.runtimeAnimatorController!=controller)return Fail("ui.animation.controller.changed",ErrorKind.InvalidState);
            animator.updateMode=AnimatorUpdateMode.UnscaledTime;animator.applyRootMotion=false;animator.enabled=true;
            generation++;animator.SetBool(parameter,visible);return Result.Success();
        }
        // Waits for a named non-looping state to actually finish. No arbitrary delay or animation-event authority.
        public async UniTask<Result> PlayAsync(bool visible,int fullPathHash,int layer,OperationContext wait)
        {
            var start=System.Diagnostics.Stopwatch.GetTimestamp();
            if(!PlayerLoopHelper.IsMainThread)return Fail("ui.animation.thread",ErrorKind.InvalidState);
            var interrupted=Interrupted(wait,start);if(interrupted.HasValue)return interrupted.Value;
            if(stopped)return Fail("ui.animation.closed",ErrorKind.InvalidState);
            if(animator==null)return Fail("ui.animation.target",ErrorKind.Unavailable);
            if(layer<0||layer>=animator.layerCount||!animator.HasState(layer,fullPathHash))return Fail("ui.animation.state",ErrorKind.InvalidArgument);
            var started=SetVisible(visible);if(!started.IsSuccess)return started;var own=generation;
            while(true)
            {
                interrupted=Interrupted(wait,start);if(interrupted.HasValue)return interrupted.Value;
                if(stopped||own!=generation)return Fail("ui.animation.superseded",ErrorKind.Canceled);
                if(animator==null||!animator.isActiveAndEnabled)return Fail("ui.animation.target",ErrorKind.Unavailable);
                if(animator.runtimeAnimatorController!=controller)return Fail("ui.animation.controller.changed",ErrorKind.InvalidState);
                var state=animator.GetCurrentAnimatorStateInfo(layer);
                if(state.fullPathHash==fullPathHash)
                {
                    if(state.loop)return Fail("ui.animation.loop",ErrorKind.NotSupported);
                    if(!animator.IsInTransition(layer)&&state.normalizedTime>=1f)return Result.Success();
                }
                await UniTask.Yield();
            }
        }
        private static Result? Interrupted(OperationContext wait,long start)
        {
            if(wait.Cancellation.IsCancellationRequested)return Fail("ui.animation.canceled",ErrorKind.Canceled);
            if(wait.Timeout!=System.Threading.Timeout.InfiniteTimeSpan&&(System.Diagnostics.Stopwatch.GetTimestamp()-start)/(double)System.Diagnostics.Stopwatch.Frequency>=wait.Timeout.TotalSeconds)return Fail("ui.animation.timeout",ErrorKind.Timeout);
            return null;
        }
        public UniTask<Result> CleanupAsync(CleanupContext cleanup)
        {
            if(!PlayerLoopHelper.IsMainThread)return UniTask.FromResult(Fail("ui.animation.thread",ErrorKind.InvalidState));
            stopped=true;
            if(animator!=null){animator.enabled=false;}
            return UniTask.FromResult(Result.Success());
        }
        private static Result Fail(string code,ErrorKind kind)=>Result.Failure(new FrameworkError(kind,code));
        private static Result<AnimatorPresentation> Reject(string code,ErrorKind kind)=>Result<AnimatorPresentation>.Failure(new FrameworkError(kind,code));
    }
}
