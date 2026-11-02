using MiniFramework.Contracts;
namespace MiniFramework.Contracts
{
    public enum ConfigState { Uninitialized, Loading, Ready, Failed, Closed }
    public interface IConfigPackageReadiness:IPackageReadiness { string Schema {get;} string ConfigVersion {get;} }
    public interface IConfigService<T> where T:class
    {
        ConfigState State {get;}
        Result<T> Get();
    }
}
