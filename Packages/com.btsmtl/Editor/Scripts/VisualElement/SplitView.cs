using UnityEngine.UIElements;

namespace BTSMTL.Editor
{
    public class SplitView : TwoPaneSplitView
    {
        public new class UxmlFactory : UxmlFactory<SplitView, UxmlTraits> { }
        public SplitView() { }
    }
}