using Cysharp.Threading.Tasks;
namespace MiniFramework.Contracts.S1
{
    // S1 compile/composition stub only. S3 replaces it with real UIKit contracts.
    public interface IPanelContract { }
    public interface IUiContractStub
    {
        UniTask<Result<TPanel>> OpenAsync<TPanel>(OperationContext operation) where TPanel : class, IPanelContract;
    }
}
