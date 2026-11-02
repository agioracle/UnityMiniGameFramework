using Cysharp.Threading.Tasks;
using Game.Access;
using MiniFramework.Contracts;
using MiniFramework.UI;
namespace Game.Features
{
    public sealed partial class SampleHomePanel:UIPanel
    {
        ILocalSampleCommands commands;
        public int ClickCount {get;private set;}
        public void Configure(ILocalSampleCommands value,string status,bool recovery){commands=value;Status.text=status;StartButton.interactable=!recovery;RetryButton.gameObject.SetActive(recovery);}
        public override UniTask<Result> OnOpenAsync(IScope scope,OperationContext operation)
        {
            var first=UiSubscription.Bind(scope,"home.start",()=>StartButton.onClick.AddListener(StartRun),()=>StartButton.onClick.RemoveListener(StartRun));
            if(!first.IsSuccess)return UniTask.FromResult(Result.Failure(first.Error));
            var retry=UiSubscription.Bind(scope,"home.retry",()=>RetryButton.onClick.AddListener(Retry),()=>RetryButton.onClick.RemoveListener(Retry));
            return UniTask.FromResult(retry.IsSuccess?Result.Success():Result.Failure(retry.Error));
        }
        void StartRun(){ClickCount++;commands?.RequestLevel(1);}
        void Retry(){commands?.RequestRetry();}
        public override UniTask<Result> OnCloseAsync(CleanupContext cleanup){commands=null;return UniTask.FromResult(Result.Success());}
    }
}
