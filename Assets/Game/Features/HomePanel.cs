using System;
using Cysharp.Threading.Tasks;
using Game.Access;
using MiniFramework.Contracts;
using MiniFramework.Contracts.S1;
namespace Game.Features
{
    // Contract-only UI stub, no scene changes or dependency on Boot/Runtime/Providers.
    public sealed class HomePanel : IPanelContract
    {
        private readonly GameServices game;
        public HomePanel(GameServices game) { this.game = game ?? throw new ArgumentNullException(nameof(game)); }
        // The concrete generic argument stays in Features; Access knows only the contract.
        public static UniTask<Result<HomePanel>> OpenAsync(GameServices game, OperationContext operation) => game.OpenAsync<HomePanel>(operation);
        public UniTask<Result<string>> RefreshAsync(OperationContext operation) => game.ReadHomeTitleAsync(operation);
    }
}
