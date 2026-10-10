using UnityEditor;
using UnityEngine.UIElements;

namespace BTSMTL.Editor
{
    public class DragHandle
    {
        public static string DragName;

        bool m_GotMouseDown;
        VisualElement m_DragItem;
        VisualElement m_DragData;

        public void Init(VisualElement target)
        {
            m_DragItem = target;
            m_DragData = target;

            m_DragItem.RegisterCallback<MouseDownEvent>(OnMouseDownEvent);
            m_DragItem.RegisterCallback<MouseMoveEvent>(OnMouseMoveEvent);
            m_DragItem.RegisterCallback<MouseUpEvent>(OnMouseUpEvent);
        }
        public void Init(VisualElement dragItem, VisualElement dragTarget)
        {
            Init(dragItem);
            m_DragData = dragTarget;
        }
        public void Dispose()
        {
            m_DragItem?.UnregisterCallback<MouseDownEvent>(OnMouseDownEvent);
            m_DragItem?.UnregisterCallback<MouseMoveEvent>(OnMouseMoveEvent);
            m_DragItem?.UnregisterCallback<MouseUpEvent>(OnMouseUpEvent);
        }

        void OnMouseDownEvent(MouseDownEvent e)
        {
            if (e.button == 0)
            {
                m_GotMouseDown = true;
            }
            e.StopImmediatePropagation();
        }
        void OnMouseMoveEvent(MouseMoveEvent e)
        {
            if (m_GotMouseDown && e.pressedButtons == 1)
            {
                StartDraggingBox();
                m_GotMouseDown = false;
            }
        }
        void OnMouseUpEvent(MouseUpEvent e)
        {
            if (m_GotMouseDown && e.button == 0)
            {
                m_GotMouseDown = false;
            }
        }
        void StartDraggingBox()
        {
            DragName = m_DragData.GetType().Name;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DragName, m_DragData);
            DragAndDrop.StartDrag(DragName);
        }
    }
}