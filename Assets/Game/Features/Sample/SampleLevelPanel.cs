using Cysharp.Threading.Tasks;
using Game.Access;
using MiniFramework.Contracts;
using MiniFramework.UI;
namespace Game.Features
{
    public sealed partial class SampleLevelPanel:UIPanel
    {
        ILocalSampleCommands commands;
        public void Configure(ILocalSampleCommands value,string status){commands=value;Status.text=status;}
        public override UniTask<Result> OnOpenAsync(IScope scope,OperationContext operation)
        {
            var back=UiSubscription.Bind(scope,"level.back",()=>BackButton.onClick.AddListener(Back),()=>BackButton.onClick.RemoveListener(Back));
            if(!back.IsSuccess)return UniTask.FromResult(Result.Failure(back.Error));
            var collect=UiSubscription.Bind(scope,"level.collect",()=>CollectButton.onClick.AddListener(Collect),()=>CollectButton.onClick.RemoveListener(Collect));
            if(!collect.IsSuccess)return UniTask.FromResult(Result.Failure(collect.Error));
            var mute=UiSubscription.Bind(scope,"level.mute",()=>MuteButton.onClick.AddListener(Mute),()=>MuteButton.onClick.RemoveListener(Mute));
            return UniTask.FromResult(mute.IsSuccess?Result.Success():Result.Failure(mute.Error));
        }
        void Back(){commands?.RequestHome();}void Collect(){commands?.Collect();}void Mute(){commands?.ToggleMute();}
        public override UniTask<Result> OnCloseAsync(CleanupContext cleanup){commands=null;return UniTask.FromResult(Result.Success());}
    }
}
