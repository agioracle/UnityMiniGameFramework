using System;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
namespace MiniFramework.Providers.Fake
{
    internal static class FakeThread
    {
        internal static void Check(){if(!PlayerLoopHelper.IsMainThread)throw new InvalidOperationException("FakeProvider is main-thread controlled.");}
        internal static Result<T> Reject<T>(string code,ErrorKind kind=ErrorKind.InvalidState)=>Result<T>.Failure(new FrameworkError(kind,code));
    }
    public sealed class ControlledOperation<T> : IProviderOperation<T>
    {
        private readonly UniTaskCompletionSource<Result<T>> completion=new UniTaskCompletionSource<Result<T>>();
        private readonly Action<Result<T>> beforePublish;
        public string Id { get; }
        public BackendState State { get; private set; }=BackendState.Running;
        public UniTask<Result<T>> Completion=>completion.Task;
        internal ControlledOperation(string id,Action<Result<T>> beforePublish=null){Id=id;this.beforePublish=beforePublish;}
        public bool Complete(Result<T> result)
        {
            FakeThread.Check();if(State!=BackendState.Running)return false;
            State=result.IsSuccess?BackendState.Succeeded:BackendState.Failed;
            beforePublish?.Invoke(result);completion.TrySetResult(result);return true;
        }
    }
}
