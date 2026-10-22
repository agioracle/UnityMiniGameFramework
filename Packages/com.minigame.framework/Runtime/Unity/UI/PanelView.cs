using System;
using UnityEngine;

namespace MiniGame.Framework
{
    [DisallowMultipleComponent]
    public class PanelView : MonoBehaviour
    {
        [SerializeField] private int layer;
        [SerializeField] private bool opaqueCover;
        [SerializeField, Min(0)] private float cacheSeconds;
        [SerializeField] private GameObject defaultSelection;
        private GameObject lastSelection;
        private bool covered;
        internal GameObject Selection => lastSelection ? lastSelection : defaultSelection;
        internal void RememberSelection(GameObject selection)
        {
            if (selection && selection.transform.IsChildOf(transform)) lastSelection = selection;
        }
        public int Layer => layer;
        public bool OpaqueCover => opaqueCover;
        public float CacheSeconds => cacheSeconds;
        public event Action<bool> CoveredChanged;
        internal void SetCovered(bool value)
        {
            bool changed = covered != value;
            covered = value;
            if (gameObject.activeSelf == value) gameObject.SetActive(!value);
            if (changed)
            {
                try { CoveredChanged?.Invoke(value); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }
    }
}
