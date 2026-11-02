using MiniFramework.Contracts;
namespace MiniFramework.Providers.WeChat.Storage
{
    // Optional actual file-handle lease. A failed read can retain cleanup without replaying the read.
    public interface IWeChatStorageCleanup
    {
        bool CleanupPending { get; }
        Result RetryCleanup();
    }
}
