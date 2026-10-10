using System.Collections.Generic;
using UnityEngine.UIElements;

namespace BTSMTL.Editor
{
    public interface ISelection
    {
        VisualElement ContentContainer { get; }
        IReadOnlyList<ISelectable> Elements { get; }
        IReadOnlyList<ISelectable> Selections { get; }
        void AddToSelection(ISelectable selectable);
        void RemoveFromSelection(ISelectable selectable);
        void ClearSelection();
    }
}
