// <mini-ui-generated schema=1 type=Game.Features.SampleLevelPanel>
namespace Game.Features
{
    public partial class SampleLevelPanel : global::MiniFramework.UI.IUiGeneratedBindings
    {
        [global::UnityEngine.SerializeField, global::MiniFramework.UI.UiBindingPath("Back")]
        private global::UnityEngine.UI.Button __ui_BackButton;
        public global::UnityEngine.UI.Button BackButton => __ui_BackButton;
        [global::UnityEngine.SerializeField, global::MiniFramework.UI.UiBindingPath("Collect")]
        private global::UnityEngine.UI.Button __ui_CollectButton;
        public global::UnityEngine.UI.Button CollectButton => __ui_CollectButton;
        [global::UnityEngine.SerializeField, global::MiniFramework.UI.UiBindingPath("Mute")]
        private global::UnityEngine.UI.Button __ui_MuteButton;
        public global::UnityEngine.UI.Button MuteButton => __ui_MuteButton;
        [global::UnityEngine.SerializeField, global::MiniFramework.UI.UiBindingPath("Status")]
        private global::UnityEngine.UI.Text __ui_Status;
        public global::UnityEngine.UI.Text Status => __ui_Status;
        public global::MiniFramework.Contracts.Result ValidateGeneratedBindings()
        {
            if (__ui_BackButton == null) return global::MiniFramework.Contracts.Result.Failure(new global::MiniFramework.Contracts.FrameworkError(global::MiniFramework.Contracts.ErrorKind.Integrity, "ui.binding.missing", "Game.Features.SampleLevelPanel.BackButton"));
            if (__ui_CollectButton == null) return global::MiniFramework.Contracts.Result.Failure(new global::MiniFramework.Contracts.FrameworkError(global::MiniFramework.Contracts.ErrorKind.Integrity, "ui.binding.missing", "Game.Features.SampleLevelPanel.CollectButton"));
            if (__ui_MuteButton == null) return global::MiniFramework.Contracts.Result.Failure(new global::MiniFramework.Contracts.FrameworkError(global::MiniFramework.Contracts.ErrorKind.Integrity, "ui.binding.missing", "Game.Features.SampleLevelPanel.MuteButton"));
            if (__ui_Status == null) return global::MiniFramework.Contracts.Result.Failure(new global::MiniFramework.Contracts.FrameworkError(global::MiniFramework.Contracts.ErrorKind.Integrity, "ui.binding.missing", "Game.Features.SampleLevelPanel.Status"));
            return global::MiniFramework.Contracts.Result.Success();
        }
    }
}
