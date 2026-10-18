using System;

namespace BTSMTL.Authoring.Graph
{
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
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
        public bool HasDefaultValue { get; set; }
        public string DefaultValue { get; set; }
        public string PickerKind { get; set; }
        public string PickerOwnerField { get; set; }
        public string ReadMember { get; set; }
        public string ReadOperation { get; set; }
        public string ChoicesMember { get; set; }
        public string ResourceDomain { get; set; }
        public Type ResourceContextType { get; set; }
        public string ResourceContextMember { get; set; }
        public bool ResourceNavigation { get; set; }
        public string ResourceAssetBindingMember { get; set; }
        public string WriteMember { get; set; }
        public string WriteParameter { get; set; }
        public string DefaultMember { get; set; }
        public bool ReadOnly { get; set; }
        public bool AuthoringVisible { get; set; } = true;
        public string VisibleWhenField { get; set; }
        public string VisibleWhenValue { get; set; }
    }
}
