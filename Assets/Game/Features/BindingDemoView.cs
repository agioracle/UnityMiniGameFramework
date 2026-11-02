using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.UI;
namespace Game.Features
{
    // User logic remains separate from the generated fields.
    public sealed partial class BindingDemoView : UIPanel
    {
        public int ClickCount {get;private set;}
        public override UniTask<Result> OnOpenAsync(IScope viewScope,OperationContext operation)
        {
            var binding=UiSubscription.Bind(viewScope,"demo.action",()=>ActionButton.onClick.AddListener(Clicked),()=>ActionButton.onClick.RemoveListener(Clicked));
            return UniTask.FromResult(binding.IsSuccess?Result.Success():Result.Failure(binding.Error));
        }
        private void Clicked(){ClickCount++;}
    }
}
