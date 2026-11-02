using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts
{
    public enum UiSessionState { Opening, Open, Closing, CleanupFailed, Closed }
    public interface IUiPanel
    {
        UniTask<Result> OnOpenAsync(IScope viewScope,OperationContext operation);
        UniTask<Result> OnCloseAsync(CleanupContext cleanup);
    }
    public interface IUiSession<out T> where T:class,IUiPanel
    {
        string Id {get;} T Panel {get;} IScope ViewScope {get;} UiSessionState State {get;}
        // Safe inside lifecycle hooks; never await your own Close from OnOpen/OnClose.
        void RequestClose();
        UniTask<Result> CloseAsync(OperationContext wait);
        UniTask<Result> RetryCloseAsync(OperationContext wait);
    }
    public interface IUiService
    {
        UniTask<Result<IUiSession<T>>> OpenAsync<T>(IScope owner,OperationContext wait) where T:class,IUiPanel;
        // Includes opening sessions; multi-instance closes all of this type belonging to this owner.
        UniTask<Result> CloseAsync<T>(IScope owner,OperationContext wait) where T:class,IUiPanel;
        UniTask<Result> BackAsync(IScope owner,OperationContext wait);
        UniTask<Result> TrimCacheAsync(OperationContext wait);
        UniTask<Result> RetryCleanupAsync(OperationContext wait);
    }
}
