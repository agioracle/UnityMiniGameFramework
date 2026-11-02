using System;
using MiniFramework.Storage;

namespace MiniFramework.Providers.WeChat.Storage
{
    /// <summary>Whole-save buffering budget. Does not describe platform quota or available disk.</summary>
    public sealed class WeChatStorageOptions
    {
        public const int DefaultMaxContainerBytes=64*1024;
        public int MaxContainerBytes {get;}
        public WeChatStorageOptions(int maxContainerBytes=DefaultMaxContainerBytes)
        {
            if(maxContainerBytes<256||maxContainerBytes>StorageLimits.MaxSupportedContainerBytes)
                throw new ArgumentOutOfRangeException(nameof(maxContainerBytes));
            MaxContainerBytes=maxContainerBytes;
        }
        public StorageLimits CreateServiceLimits(int maxModules=32,int? maxPayloadBytes=null,int maxOperations=16)
            =>new StorageLimits(maxModules,maxPayloadBytes??Math.Min(64*1024,MaxContainerBytes),MaxContainerBytes,maxOperations);
    }
    /// <summary>Optional port capacity declaration. Legacy custom ports must enforce their caller's budget.</summary>
    public interface IWeChatStorageFileLimits
    {
        int MaxFileBytes {get;}
    }
}
