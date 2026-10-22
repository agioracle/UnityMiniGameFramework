using UnityEngine;

namespace MiniGame.Framework
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaLayout : MonoBehaviour
    {
        private RectTransform rect;
        private Rect previous;
        private Vector2 size;
        private void Awake() { rect = (RectTransform)transform; Apply(); }
        private void Update()
        {
            if (previous != Screen.safeArea || size.x != Screen.width || size.y != Screen.height) Apply();
        }
        private void Apply()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;
            previous = Screen.safeArea;
            size = new Vector2(Screen.width, Screen.height);
            rect.anchorMin = new Vector2(previous.xMin / size.x, previous.yMin / size.y);
            rect.anchorMax = new Vector2(previous.xMax / size.x, previous.yMax / size.y);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
