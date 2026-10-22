using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MiniGame.Framework
{
    public enum PointerGesture { Began, Dragged, Tapped, Ended, Cancelled }

    public readonly struct PointerAction
    {
        public readonly int PointerId;
        public readonly Vector2 Position;
        public readonly Vector2 Delta;
        public readonly PointerGesture Gesture;
        public PointerAction(int pointerId, Vector2 position, Vector2 delta, PointerGesture gesture)
        {
            PointerId = pointerId; Position = position; Delta = delta; Gesture = gesture;
        }
    }

    public sealed class LegacyPointerInput
    {
        private struct Pointer
        {
            public int id;
            public bool active;
            public bool consumed;
            public bool dragged;
            public Vector2 origin;
            public Vector2 last;
        }
        private readonly Pointer[] pointers;
        private readonly float dragThresholdSquared;
        private readonly List<RaycastResult> hits = new List<RaycastResult>(16);
        private PointerEventData pointerEvent;
        private EventSystem pointerEventOwner;
        public event Action<PointerAction> Action;

        public LegacyPointerInput(int capacity = 10, float dragThresholdPixels = 12)
        {
            if (capacity < 1 || capacity > 32) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (dragThresholdPixels < 0 || float.IsNaN(dragThresholdPixels) || float.IsInfinity(dragThresholdPixels) || dragThresholdPixels > 10000)
                throw new ArgumentOutOfRangeException(nameof(dragThresholdPixels));
            pointers = new Pointer[capacity];
            dragThresholdSquared = dragThresholdPixels * dragThresholdPixels;
        }

        public void Poll()
        {
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch touch = Input.GetTouch(i);
                    Feed(touch.fingerId, touch.position, touch.phase, touch.phase == TouchPhase.Began && IsConsumedByUi(touch.fingerId, touch.position));
                }
                return;
            }
            Vector2 position = Input.mousePosition;
            if (Input.GetMouseButtonDown(0)) Feed(-1, position, TouchPhase.Began, IsConsumedByUi(-1, position));
            else if (Input.GetMouseButtonUp(0)) Feed(-1, position, TouchPhase.Ended);
            else if (Input.GetMouseButton(0)) Feed(-1, position, TouchPhase.Moved);
        }

        public bool IsConsumedByUi(int pointerId, Vector2 position)
        {
            if (float.IsNaN(position.x) || float.IsInfinity(position.x) || float.IsNaN(position.y) || float.IsInfinity(position.y))
                throw new ArgumentException("Pointer position must be finite.", nameof(position));
            EventSystem input = EventSystem.current;
            if (!input) return false;
            if (!pointerEventOwner || pointerEventOwner != input)
            {
                pointerEventOwner = input;
                pointerEvent = new PointerEventData(input);
            }
            pointerEvent.Reset();
            pointerEvent.pointerId = pointerId;
            pointerEvent.position = position;
            hits.Clear();
            input.RaycastAll(pointerEvent, hits);
            bool consumed = false;
            for (int i = 0; i < hits.Count; i++)
                if (hits[i].module is UnityEngine.UI.GraphicRaycaster) { consumed = true; break; }
            hits.Clear();
            return consumed;
        }

        public bool Feed(int pointerId, Vector2 position, TouchPhase phase, bool consumedByUi = false)
        {
            if (float.IsNaN(position.x) || float.IsInfinity(position.x) || float.IsNaN(position.y) || float.IsInfinity(position.y))
                throw new ArgumentException("Pointer position must be finite.", nameof(position));
            int index = -1;
            for (int i = 0; i < pointers.Length; i++) if (pointers[i].active && pointers[i].id == pointerId) { index = i; break; }
            if (phase == TouchPhase.Began)
            {
                if (index >= 0) return false;
                for (int i = 0; i < pointers.Length; i++) if (!pointers[i].active) { index = i; break; }
                if (index < 0) return false;
                pointers[index] = new Pointer { id = pointerId, active = true, consumed = consumedByUi, origin = position, last = position };
                if (!consumedByUi) Action?.Invoke(new PointerAction(pointerId, position, Vector2.zero, PointerGesture.Began));
                return true;
            }
            if (index < 0) return false;
            Pointer pointer = pointers[index];
            Vector2 delta = position - pointer.last;
            pointer.last = position;
            pointer.dragged |= (position - pointer.origin).sqrMagnitude > dragThresholdSquared;
            if (phase == TouchPhase.Ended || phase == TouchPhase.Canceled)
            {
                pointers[index] = default;
                if (!pointer.consumed)
                    Action?.Invoke(new PointerAction(pointerId, position, delta, phase == TouchPhase.Canceled ? PointerGesture.Cancelled : pointer.dragged ? PointerGesture.Ended : PointerGesture.Tapped));
            }
            else
            {
                pointers[index] = pointer;
                if (!pointer.consumed && pointer.dragged && phase == TouchPhase.Moved)
                    Action?.Invoke(new PointerAction(pointerId, position, delta, PointerGesture.Dragged));
            }
            return true;
        }

        public void Reset()
        {
            Array.Clear(pointers, 0, pointers.Length);
        }
    }
}
