using System;

namespace BTSMTL.Authoring.Graph
{
    public enum GraphAuthoringFieldValueKind : byte
    {
        String = 1,
        Boolean = 2,
        Integer = 3,
        Float = 4,
        Vector2 = 5,
        Vector3 = 6,
        Quaternion = 7,
        Enum = 8,
        AssetReference = 9,
        IdentityReference = 10,
        Object = 11
    }

    [Flags]
    public enum GraphAuthoringFieldAccess : byte
    {
        None = 0,
        AuthoringRead = 1,
        AuthoringWrite = 2,
        ReferenceRead = 4,
        DiagnosticRead = 8
    }

    public enum GraphAuthoringDetailsSection : byte
    {
        Authoring = 1,
        RuntimeInputs = 2,
        AppliedValues = 3,
        References = 4,
        Diagnostics = 5
    }

    public enum GraphAuthoringFieldInteractionPolicy : byte
    {
        Unclassified = 0,
        Structural = 1,
        TunableDefault = 2,
        RuntimeInput = 3,
        DerivedReadOnly = 4
    }

    public enum GraphAuthoringFieldCompilationImpact : byte
    {
        Unclassified = 0,
        Structure = 1,
        Parameter = 2,
        Editor = 3
    }

    public readonly struct GraphAuthoringFieldCompilation
    {
        public GraphAuthoringFieldCompilation(GraphAuthoringFieldCompilationImpact impact, string unit)
        {
            Impact = impact;
            Unit = unit;
        }

        public GraphAuthoringFieldCompilationImpact Impact { get; }
        public string Unit { get; }
    }

    public enum GraphAuthoringFieldApplyTiming : byte
    {
        NextFrame = 0,
        NextActivation = 1
    }

    public enum GraphAuthoringFieldStatePolicy : byte
    {
        PreserveState = 0,
        ResetOwnerState = 1
    }
}
