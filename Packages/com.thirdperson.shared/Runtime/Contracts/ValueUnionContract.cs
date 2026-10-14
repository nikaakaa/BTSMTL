using System;

namespace ThirdPersonSimulation
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class ValueOperationsAttribute : Attribute
    {
        public bool Resources { get; set; } = true;
        public bool ExplicitEquality { get; set; }
        public bool ExplicitResources { get; set; }
        public bool OwnsMemberResources { get; set; }
        public string RetainMethod { get; set; } = "Retain";
        public string ReleaseMethod { get; set; } = "Release";
        public string CopyMethod { get; set; }
        public string ClearMethod { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ValueEqualityMemberAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ValueEqualityIgnoreAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
    public sealed class ValuePayloadAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class ValuePresenceAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ValueDefaultAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class ValueTextAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class ValueBufferAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class ValueBorrowedSequenceAttribute : Attribute
    {
        public ValueBorrowedSequenceAttribute(Type storage) => Storage = storage;
        public Type Storage { get; }
        public bool HoldStorage { get; set; } = true;
        public bool RetainElementsOnRead { get; set; }
        public bool RetainStorageOnRead { get; set; }
        public bool DeclaredText { get; set; }
        public bool SortedUnique { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ValueBorrowedObjectAttribute : Attribute
    {
        public ValueBorrowedObjectAttribute(Type storage) => Storage = storage;
        public Type Storage { get; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ValueValidationAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ValueTextRulesAttribute : Attribute
    {
        public bool Required { get; set; }
        public bool Trim { get; set; }
        public bool NullToEmpty { get; set; }
        public bool Declared { get; set; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ValueResourceAttribute : Attribute
    {
        public ValueResourceAttribute(string retain = "Retain", string release = "Release")
        {
            Retain = retain;
            Release = release;
        }

        public string Retain { get; }
        public string Release { get; }
        public bool Optional { get; set; }
        public bool Elements { get; set; }
    }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public sealed class ValueStorageAttribute : Attribute
    {
        public bool Construct { get; set; }
        public bool UseConstructor { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class ValueStorageOwnerAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ValueStorageMemberAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ValueReferenceAccessAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ValueUpdateAttribute : Attribute { }

    public readonly struct ValueConstruction { }

    public interface IValueResources
    {
        void Retain();
        void Release();
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ValueSnapshotAttribute : Attribute
    {
        public string RestoreMethod { get; set; }
        public string CopyMethod { get; set; }
        public string ClearMethod { get; set; }
        public bool FixedKeys { get; set; }
    }

    public interface IValueSnapshotSource
    {
        IValueSnapshotCopy CreateValueSnapshot();
        void CaptureValueSnapshot(IValueResources snapshot);
        void RestoreValueSnapshot(IValueResources snapshot);
    }

    public interface IValueSnapshotCopy : IValueResources
    {
        void CopyFrom(IValueResources source);
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class ValueUnionOmittedAttribute : Attribute
    {
    }

    [ValueUnionOmitted]
    public readonly struct CanonicalUnavailable
    {
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class ValueUnionCaseAttribute : Attribute
    {
        public ValueUnionCaseAttribute() { }
        public ValueUnionCaseAttribute(Type payload) => Payload = payload;
        public Type Payload { get; }
        public bool NormalizeZero { get; set; }
        public bool NormalizeNull { get; set; }
        public string DefaultMember { get; set; }
    }

    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Enum)]
    public sealed class ValueUnionTemplateAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class ValueUnionAttribute : Attribute
    {
        public ValueUnionAttribute(Type definition) => Definition = definition;
        public Type Definition { get; }
    }
}
