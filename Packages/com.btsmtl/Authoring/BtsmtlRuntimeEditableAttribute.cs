using System;

namespace BTSMTL.Authoring
{
    public enum BtsmtlRuntimeValueKind : byte
    {
        InputValue,
        InputRequest,
        BlackboardValue,
        ActionEvent,
        AttributeValue,
        AnimationVariable,
        Nested,
        Variable
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, Inherited = true)]
    public sealed class BtsmtlRuntimeEditableAttribute : Attribute
    {
        public BtsmtlRuntimeEditableAttribute(BtsmtlRuntimeValueKind kind, string referenceMember = "")
        {
            Kind = kind;
            ReferenceMember = referenceMember;
        }

        public BtsmtlRuntimeValueKind Kind { get; }
        public string ReferenceMember { get; }
        public string OwnerMember { get; set; }
    }
}
