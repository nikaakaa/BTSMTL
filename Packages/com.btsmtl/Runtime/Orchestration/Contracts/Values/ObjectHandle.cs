using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [Serializable, ValueStorage(UseConstructor = true)]
    public readonly partial struct ObjectHandle : IEquatable<ObjectHandle>
    {
        public ObjectHandle(ulong value) => Value = value;

        public ulong Value { get; }
        public bool IsEmpty => Value == 0;
        [ValueDefault] public static ObjectHandle Empty => default;

        public bool Equals(ObjectHandle other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ObjectHandle other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static bool operator ==(ObjectHandle left, ObjectHandle right) => left.Equals(right);
        public static bool operator !=(ObjectHandle left, ObjectHandle right) => !left.Equals(right);
    }
}
