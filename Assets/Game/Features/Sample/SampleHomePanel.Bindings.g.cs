// <mini-ui-generated schema=1 type=Game.Features.SampleHomePanel>
namespace Game.Features
{
    public partial class SampleHomePanel : global::MiniFramework.UI.IUiGeneratedBindings
    {
        [global::UnityEngine.SerializeField, global::MiniFramework.UI.UiBindingPath("Retry")]
        private global::UnityEngine.UI.Button __ui_RetryButton;
        public global::UnityEngine.UI.Button RetryButton => __ui_RetryButton;
        [global::UnityEngine.SerializeField, global::MiniFramework.UI.UiBindingPath("Start")]
        private global::UnityEngine.UI.Button __ui_StartButton;
        public global::UnityEngine.UI.Button StartButton => __ui_StartButton;
        [global::UnityEngine.SerializeField, global::MiniFramework.UI.UiBindingPath("Status")]
        private global::UnityEngine.UI.Text __ui_Status;
        public global::UnityEngine.UI.Text Status => __ui_Status;
        public global::MiniFramework.Contracts.Result ValidateGeneratedBindings()
        {
            if (__ui_RetryButton == null) return global::MiniFramework.Contracts.Result.Failure(new global::MiniFramework.Contracts.FrameworkError(global::MiniFramework.Contracts.ErrorKind.Integrity, "ui.binding.missing", "Game.Features.SampleHomePanel.RetryButton"));
            if (__ui_StartButton == null) return global::MiniFramework.Contracts.Result.Failure(new global::MiniFramework.Contracts.FrameworkError(global::MiniFramework.Contracts.ErrorKind.Integrity, "ui.binding.missing", "Game.Features.SampleHomePanel.StartButton"));
            if (__ui_Status == null) return global::MiniFramework.Contracts.Result.Failure(new global::MiniFramework.Contracts.FrameworkError(global::MiniFramework.Contracts.ErrorKind.Integrity, "ui.binding.missing", "Game.Features.SampleHomePanel.Status"));
            return global::MiniFramework.Contracts.Result.Success();
        }
    }
}
