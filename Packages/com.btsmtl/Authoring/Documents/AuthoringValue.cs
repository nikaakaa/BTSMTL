using System;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Documents
{
    public abstract class AuthoringValue
    {
        protected AuthoringValue(string typeId) => TypeId = typeId;
        public string TypeId { get; }
    }

    public sealed class AuthoringNullValue : AuthoringValue
    {
        public AuthoringNullValue(string typeId) : base(typeId) { }
    }

    public sealed class AuthoringScalarValue : AuthoringValue
    {
        public AuthoringScalarValue(string typeId, GraphAuthoringFieldValueKind kind, string value) : base(typeId)
        {
            Kind = kind;
            Value = value;
        }

        public GraphAuthoringFieldValueKind Kind { get; }
        public string Value { get; }
    }

    public sealed class AuthoringReferenceValue : AuthoringValue
    {
        public AuthoringReferenceValue(string typeId, AuthoringContentReference target) : base(typeId) => Target = target;
        public AuthoringContentReference Target { get; }
    }

    public sealed class AuthoringObjectValue : AuthoringValue
    {
        public AuthoringObjectValue(string typeId, ReadOnlyMemory<AuthoringNamedValue> members) : base(typeId) => Members = members;
        public ReadOnlyMemory<AuthoringNamedValue> Members { get; }
    }

    public sealed class AuthoringSequenceValue : AuthoringValue
    {
        public AuthoringSequenceValue(string typeId, ReadOnlyMemory<AuthoringValue> items) : base(typeId) => Items = items;
        public ReadOnlyMemory<AuthoringValue> Items { get; }
    }

    public readonly struct AuthoringNamedValue
    {
        public AuthoringNamedValue(GraphAuthoringFieldId fieldId, AuthoringValue value)
        {
            FieldId = fieldId;
            Value = value;
        }

        public GraphAuthoringFieldId FieldId { get; }
        public AuthoringValue Value { get; }
    }

    public readonly struct AuthoringContentReference
    {
        public AuthoringContentReference(GraphAuthoringElementId rootId, GraphAuthoringElementId documentId,
            GraphAuthoringElementId elementId)
        {
            RootId = rootId;
            DocumentId = documentId;
            ElementId = elementId;
        }

        public GraphAuthoringElementId RootId { get; }
        public GraphAuthoringElementId DocumentId { get; }
        public GraphAuthoringElementId ElementId { get; }
    }
}
