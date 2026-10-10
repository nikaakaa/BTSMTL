using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Editor
{
    public interface ISelectable
    {
        public ISelection SelectionContainer { get; set; }
        //
        // ժҪ:
        //     Check if element is selectable.
        //
        // ���ؽ��:
        //     True if selectable. False otherwise.
        bool IsSelectable();

        //
        // ժҪ:
        //     Check if selection overlaps rectangle.
        //
        // ����:
        //   rectangle:
        //     Rectangle to check.
        //
        // ���ؽ��:
        //     True if it overlaps. False otherwise.
        bool Overlaps(Rect rectangle);

        //
        // ժҪ:
        //     Select element.
        //
        // ����:
        //   selectionContainer:
        //     Container in which selection is tracked.
        //
        //   additive:
        //     True if selection is additive. False otherwise.
        void Select();

        //
        // ժҪ:
        //     Deselect element.
        //
        // ����:
        //   selectionContainer:
        //     Container in which selection is tracked.
        void Unselect();

        //
        // ժҪ:
        //     Check if element is selected.
        //
        // ����:
        //   selectionContainer:
        //     Container in which the selection is tracked.
        //
        // ���ؽ��:
        //     True if selected. False otherwise.
        bool IsSelected();
    }
}