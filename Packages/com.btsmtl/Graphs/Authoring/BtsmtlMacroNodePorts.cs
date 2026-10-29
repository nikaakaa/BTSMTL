namespace BTSMTL.Graphs
{
    public sealed partial class BtsmtlMacroInputNode
#if BTSMTL_AUTHORING && !UNITY_EDITOR
        : FlowCanvas.Macros.MacroInputNode
#endif
    { }

    public sealed partial class BtsmtlMacroOutputNode
#if BTSMTL_AUTHORING && !UNITY_EDITOR
        : FlowCanvas.Macros.MacroOutputNode
#endif
    { }
}
