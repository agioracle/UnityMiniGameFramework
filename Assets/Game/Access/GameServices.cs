using System;
using Cysharp.Threading.Tasks;
using MiniFramework.Contracts;
using MiniFramework.Contracts.S1;
namespace Game.Access
{
    public interface IHomeService { UniTask<Result<string>> ReadTitleAsync(OperationContext operation); }
    // Instance facade; the resolver is injected by Boot, never a global service locator.
    public sealed class GameServices
    {
        private readonly IGameServices services;
        public GameServices(IGameServices services) { this.services = services ?? throw new ArgumentNullException(nameof(services)); }
        public async UniTask<Result<TPanel>> OpenAsync<TPanel>(OperationContext operation) where TPanel : class, IPanelContract
        {
            if (!services.TryGet<IUiContractStub>(out var ui)) return Result<TPanel>.Failure(
                new FrameworkError(ErrorKind.NotReady, "game.ui.not.ready"));
            return await ui.OpenAsync<TPanel>(operation);
        }
        // Production UI path. The operation returns a ViewScope-owned session, not a naked component.
        public UniTask<Result<IUiSession<TPanel>>> OpenAsync<TPanel>(IScope owner,OperationContext operation) where TPanel:class,IUiPanel
        {
            if(!services.TryGet<IUiService>(out var ui))return UniTask.FromResult(Result<IUiSession<TPanel>>.Failure(new FrameworkError(ErrorKind.NotReady,"game.ui.not.ready")));
            return ui.OpenAsync<TPanel>(owner,operation);
        }
        public Result<IInputDeviceService> InputDevice()
        {return services.TryGet<IInputDeviceService>(out var input)?Result<IInputDeviceService>.Success(input):Result<IInputDeviceService>.Failure(new FrameworkError(ErrorKind.NotReady,"game.input.not.ready"));}
        public UniTask<Result<IAudioPlayback>> PlayAudioAsync(ResourceKey key,AudioPlayOptions options,IScope owner,OperationContext operation)
        {return services.TryGet<IAudioService>(out var audio)?audio.PlayAsync(key,options,owner,operation):UniTask.FromResult(Result<IAudioPlayback>.Failure(new FrameworkError(ErrorKind.NotReady,"game.audio.not.ready")));}
        public UniTask<Result<IGameSession>> EnterSessionAsync(string id,ResourceKey? scene,OperationContext operation)
        {
            return services.TryGet<ISessionFlow>(out var flow)?flow.EnterAsync(id,scene,operation):UniTask.FromResult(Result<IGameSession>.Failure(new FrameworkError(ErrorKind.NotReady,"game.flow.not.ready")));
        }
        public Result<T> ReadConfig<T>() where T:class
        {
            return services.TryGet<IConfigService<T>>(out var config)?config.Get():Result<T>.Failure(new FrameworkError(ErrorKind.NotReady,"game.config.not.ready"));
        }
        public async UniTask<Result<string>> ReadHomeTitleAsync(OperationContext operation)
        {
            if (!services.TryGet<IHomeService>(out var home)) return Result<string>.Failure(
                new FrameworkError(ErrorKind.NotReady, "game.home.not.ready"));
            return await home.ReadTitleAsync(operation);
        }
    }
}
