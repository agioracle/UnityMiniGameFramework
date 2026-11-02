using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.UI;
using Game.Access;
namespace Game.Features
{
    // Actual MonoBehaviour compile/composition example; generated fields arrive in S3-02.
    public sealed class HomeView : UIPanel
    {
        public static UniTask<Result<IUiSession<HomeView>>> OpenAsync(GameServices game,IScope owner,OperationContext wait)=>game.OpenAsync<HomeView>(owner,wait);
    }
}
