using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Access;
using Game.Features;
using MiniFramework.Contracts;
using MiniFramework.Contracts.S1;
using MiniFramework.Runtime;
namespace Game.Boot
{
    public sealed class LocalGame
    {
        private readonly GameContext context;
        public HomePanel Home { get; }
        public GameServices Access { get; }
        public GameContextState State => context.State;
        public LocalGame(string title)
        {
            if (title == null) throw new ArgumentNullException(nameof(title));
            context = new GameContext(new[] { new ModuleRegistration("home", Array.Empty<string>(),
                () => new HomeModule(title, () => new HomePanel(Access)), typeof(IHomeService), typeof(IUiContractStub)) },
                new CleanupContext(CancellationToken.None, Timeout.InfiniteTimeSpan));
            Access = new GameServices(context); Home = new HomePanel(Access);
        }
        public UniTask<Result> StartAsync(OperationContext operation) => context.StartAsync(operation);
        public UniTask<Result> CloseAsync(OperationContext wait) => context.CloseAsync(wait);
        private sealed class HomeModule : IModule, IHomeService, IUiContractStub
        {
            private readonly string title;
            private readonly Func<HomePanel> createPanel;
            public HomeModule(string title, Func<HomePanel> createPanel) { this.title = title; this.createPanel = createPanel; }
            public UniTask<Result<TPanel>> OpenAsync<TPanel>(OperationContext operation) where TPanel : class, IPanelContract
            {
                if (operation.Cancellation.IsCancellationRequested) return UniTask.FromResult(Result<TPanel>.Failure(new FrameworkError(ErrorKind.Canceled, "ui.stub.canceled")));
                if (operation.Timeout == TimeSpan.Zero) return UniTask.FromResult(Result<TPanel>.Failure(new FrameworkError(ErrorKind.Timeout, "ui.stub.timeout")));
                if (typeof(TPanel) != typeof(HomePanel)) return UniTask.FromResult(Result<TPanel>.Failure(new FrameworkError(ErrorKind.NotSupported, "ui.stub.panel.unsupported")));
                // No GameObject, rendering, asset ownership, or real UIKit behavior is simulated here.
                return UniTask.FromResult(Result<TPanel>.Success(createPanel() as TPanel));
            }
            public string Id => "home";
            public IReadOnlyList<string> Dependencies => Array.Empty<string>();
            public UniTask<Result> InitializeAsync(IModuleContext context, OperationContext operation) => UniTask.FromResult(Result.Success());
            public UniTask<Result> StopAsync(CleanupContext cleanup) => UniTask.FromResult(Result.Success());
            public UniTask<Result> DisposeAsync(CleanupContext cleanup) => UniTask.FromResult(Result.Success());
            public UniTask<Result<string>> ReadTitleAsync(OperationContext operation)
            {
                if (operation.Cancellation.IsCancellationRequested) return UniTask.FromResult(Result<string>.Failure(new FrameworkError(ErrorKind.Canceled, "home.canceled")));
                if (operation.Timeout == TimeSpan.Zero) return UniTask.FromResult(Result<string>.Failure(new FrameworkError(ErrorKind.Timeout, "home.timeout")));
                return UniTask.FromResult(Result<string>.Success(title));
            }
        }
    }
}
