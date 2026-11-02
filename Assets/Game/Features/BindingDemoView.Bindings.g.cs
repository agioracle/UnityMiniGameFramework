// <mini-ui-generated schema=1 type=Game.Features.BindingDemoView>
namespace Game.Features
{
    public partial class BindingDemoView : global::MiniFramework.UI.IUiGeneratedBindings
    {
        [global::UnityEngine.SerializeField, global::MiniFramework.UI.UiBindingPath("Action")]
        private global::UnityEngine.UI.Button __ui_ActionButton;
        public global::UnityEngine.UI.Button ActionButton => __ui_ActionButton;
        [global::UnityEngine.SerializeField, global::MiniFramework.UI.UiBindingPath("Content")]
        private global::UnityEngine.RectTransform __ui_Content;
        public global::UnityEngine.RectTransform Content => __ui_Content;
        [global::UnityEngine.SerializeField, global::MiniFramework.UI.UiBindingPath("")]
        private global::UnityEngine.CanvasGroup __ui_Group;
        public global::UnityEngine.CanvasGroup Group => __ui_Group;
        public global::MiniFramework.Contracts.Result ValidateGeneratedBindings()
        {
            if (__ui_ActionButton == null) return global::MiniFramework.Contracts.Result.Failure(new global::MiniFramework.Contracts.FrameworkError(global::MiniFramework.Contracts.ErrorKind.Integrity, "ui.binding.missing", "Game.Features.BindingDemoView.ActionButton"));
            if (__ui_Content == null) return global::MiniFramework.Contracts.Result.Failure(new global::MiniFramework.Contracts.FrameworkError(global::MiniFramework.Contracts.ErrorKind.Integrity, "ui.binding.missing", "Game.Features.BindingDemoView.Content"));
            if (__ui_Group == null) return global::MiniFramework.Contracts.Result.Failure(new global::MiniFramework.Contracts.FrameworkError(global::MiniFramework.Contracts.ErrorKind.Integrity, "ui.binding.missing", "Game.Features.BindingDemoView.Group"));
            return global::MiniFramework.Contracts.Result.Success();
        }
    }
}
