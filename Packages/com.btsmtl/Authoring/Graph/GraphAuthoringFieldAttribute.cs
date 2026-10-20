using System;

namespace BTSMTL.Authoring.Graph
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
    public sealed class GraphAuthoringFieldAttribute : Attribute
    {
        public GraphAuthoringFieldAttribute(
            string fieldId,
            GraphAuthoringFieldValueKind valueKind)
        {
            FieldId = fieldId ?? string.Empty;
            ValueKind = valueKind;
        }

        public GraphAuthoringFieldAttribute(string fieldId, Type enumType)
            : this(fieldId, GraphAuthoringFieldValueKind.Enum)
        {
            EnumType = enumType;
        }

        public GraphAuthoringFieldAttribute(
            string fieldId,
            GraphAuthoringFieldValueKind valueKind,
            params string[] allowedValues)
            : this(fieldId, valueKind)
        {
            AllowedValues = allowedValues;
        }

        public string FieldId { get; }
        public GraphAuthoringFieldValueKind ValueKind { get; }
        public GraphAuthoringFieldCompilationImpact CompilationImpact { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string DisplayName { get; set; }
        public Type EnumType { get; set; }
        public Type ObjectType { get; set; }
        public bool Optional { get; set; }
        public bool HasMinimum { get; set; }
        public double Minimum { get; set; }
        public bool HasMaximum { get; set; }
        public double Maximum { get; set; }
        public bool Finite { get; set; }
        public bool NonEmpty { get; set; }
        public string[] AllowedValues { get; set; }
        public string PickerKind { get; set; }
        public string PickerOwnerField { get; set; }
        public string ResourceDomain { get; set; }
        public Type ResourceContextType { get; set; }
        public bool ResourceNavigation { get; set; }
        public bool ReadOnly { get; set; }
        public bool ReferenceRead { get; set; }
        public GraphAuthoringFieldApplyTiming ApplyTiming { get; set; }
        public bool AuthoringVisible { get; set; } = true;
        public string VisibleWhenField { get; set; }
        public string VisibleWhenValue { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor)]
    public sealed class GraphAuthoringConfigureAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class GraphAuthoringInlineAttribute : Attribute
    {
        public GraphAuthoringInlineAttribute(string fieldId) => FieldId = fieldId;
        public string FieldId { get; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class GraphAuthoringAnnotationAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class GraphAuthoringChoicesAttribute : Attribute
    {
        public GraphAuthoringChoicesAttribute(string fieldId) => FieldId = fieldId;
        public string FieldId { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class GraphAuthoringEvaluateAttribute : Attribute
    {
        public GraphAuthoringEvaluateAttribute(string fieldId) => FieldId = fieldId;
        public string FieldId { get; }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
    public sealed class GraphAuthoringDefaultAttribute : Attribute
    {
        public GraphAuthoringDefaultAttribute(string fieldId) => FieldId = fieldId;
        public string FieldId { get; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class GraphAuthoringResourceOwnerAttribute : Attribute
    {
        public GraphAuthoringResourceOwnerAttribute(string fieldId) => FieldId = fieldId;
        public string FieldId { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class GraphAuthoringBindResourceAttribute : Attribute
    {
        public GraphAuthoringBindResourceAttribute(string fieldId) => FieldId = fieldId;
        public string FieldId { get; }
    }
}
