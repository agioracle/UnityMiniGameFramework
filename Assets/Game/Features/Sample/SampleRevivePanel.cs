using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Access;
using MiniFramework.Contracts;
using MiniFramework.UI;
using UnityEngine.UI;
namespace Game.Features
{
    public sealed class SampleRevivePanel:UIPanel
    {
        public Text State;public Button FailButton,WatchButton,RecoverButton,AbandonButton,ReopenButton;
        IReviveSampleCommands commands;CancellationToken view;
        public void Configure(IReviveSampleCommands value){commands=value;Refresh();}
        public void Refresh(){if(State&&commands!=null)State.text=commands.ReviveStatus;}
        public override UniTask<Result> OnOpenAsync(IScope scope,OperationContext operation)
        {
            view=scope.LifetimeCancellation;
            foreach(var pair in new[]{(FailButton,(UnityEngine.Events.UnityAction)Fail),(WatchButton,(UnityEngine.Events.UnityAction)Watch),(RecoverButton,(UnityEngine.Events.UnityAction)Recover),(AbandonButton,(UnityEngine.Events.UnityAction)Abandon),(ReopenButton,(UnityEngine.Events.UnityAction)Reopen)})
            {if(!pair.Item1)return UniTask.FromResult(Result.Failure(new FrameworkError(ErrorKind.Integrity,"sample.revive.binding")));var r=UiSubscription.Bind(scope,"revive."+pair.Item1.name,()=>pair.Item1.onClick.AddListener(pair.Item2),()=>pair.Item1.onClick.RemoveListener(pair.Item2));if(!r.IsSuccess)return UniTask.FromResult(Result.Failure(r.Error));}
            return UniTask.FromResult(Result.Success());
        }
        void Fail(){commands?.RequestFailure();Refresh();}void Watch(){commands?.RequestWatch(view);Refresh();}void Recover(){commands?.RequestRecoverClaim();Refresh();}void Abandon(){commands?.RequestAbandonClaim();Refresh();}void Reopen(){commands?.RequestReopen();}
        public override UniTask<Result> OnCloseAsync(CleanupContext cleanup){commands=null;view=default;return UniTask.FromResult(Result.Success());}
    }
}
