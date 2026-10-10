using ThirdPersonSimulation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation.Fixed
{
    public enum ProgramConstantKind : byte
    {
        Boolean = 1,
        Int32 = 2,
        UInt64 = 3,
        Scalar = 4,
        Vector2 = 5,
        Vector3 = 6,
        Yaw = 7,
        String = 8,
        Bytes = 9
    }

    public sealed class ProgramConstant
    {
        readonly byte[] m_Bytes;

        ProgramConstant(
            int index,
            string identity,
            ProgramConstantKind kind,
            bool boolean,
            int int32,
            ulong uint64,
            FixedScalar scalar,
            FixedVector2 vector2,
            FixedVector3 vector3,
            FixedYaw yaw,
            string text,
            byte[] bytes)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            Index = index;
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            Kind = kind;
            Boolean = boolean;
            Int32 = int32;
            UInt64 = uint64;
            Scalar = scalar;
            Vector2 = vector2;
            Vector3 = vector3;
            Yaw = yaw;
            Text = text ?? string.Empty;
            m_Bytes = bytes == null ? Array.Empty<byte>() : (byte[])bytes.Clone();
        }

        public int Index { get; }
        public string Identity { get; }
        public ProgramConstantKind Kind { get; }
        public bool Boolean { get; }
        public int Int32 { get; }
        public ulong UInt64 { get; }
        public FixedScalar Scalar { get; }
        public FixedVector2 Vector2 { get; }
        public FixedVector3 Vector3 { get; }
        public FixedYaw Yaw { get; }
        public string Text { get; }
        public ReadOnlyMemory<byte> Bytes => m_Bytes;
        public static ProgramConstant FromBoolean(int index, string identity, bool value) => new ProgramConstant(index, identity, ProgramConstantKind.Boolean, value, default, default, default, default, default, default, null, null);
        public static ProgramConstant FromInt32(int index, string identity, int value) => new ProgramConstant(index, identity, ProgramConstantKind.Int32, default, value, default, default, default, default, default, null, null);
        public static ProgramConstant FromUInt64(int index, string identity, ulong value) => new ProgramConstant(index, identity, ProgramConstantKind.UInt64, default, default, value, default, default, default, default, null, null);
        public static ProgramConstant FromScalar(int index, string identity, FixedScalar value) => new ProgramConstant(index, identity, ProgramConstantKind.Scalar, default, default, default, value, default, default, default, null, null);
        public static ProgramConstant FromVector2(int index, string identity, FixedVector2 value) => new ProgramConstant(index, identity, ProgramConstantKind.Vector2, default, default, default, default, value, default, default, null, null);
        public static ProgramConstant FromVector3(int index, string identity, FixedVector3 value) => new ProgramConstant(index, identity, ProgramConstantKind.Vector3, default, default, default, default, default, value, default, null, null);
        public static ProgramConstant FromYaw(int index, string identity, FixedYaw value) => new ProgramConstant(index, identity, ProgramConstantKind.Yaw, default, default, default, default, default, default, value, null, null);
        public static ProgramConstant FromString(int index, string identity, string value) => new ProgramConstant(index, identity, ProgramConstantKind.String, default, default, default, default, default, default, default, value, null);
        public static ProgramConstant FromBytes(int index, string identity, byte[] value) => new ProgramConstant(index, identity, ProgramConstantKind.Bytes, default, default, default, default, default, default, default, null, value);
    }

    public sealed class SimulationOperationDefinition
    {
        readonly ReadOnlyCollection<int> m_ConstantReferences;

        public SimulationOperationDefinition(
            int index,
            string identity,
            SimulationOperationCode code,
            IEnumerable<int> constantReferences,
            int integer0,
            int integer1,
            ulong unsigned0,
            FixedScalar scalar0,
            string text0,
            uint flags)
        {
            if (index < 0)
                throw new ArgumentOutOfRangeException(nameof(index));
            Index = index;
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            Code = code;
            m_ConstantReferences = ReadOnly(constantReferences);
            Integer0 = integer0;
            Integer1 = integer1;
            Unsigned0 = unsigned0;
            Scalar0 = scalar0;
            Text0 = text0 ?? string.Empty;
            Flags = flags;
        }

        public int Index { get; }
        public string Identity { get; }
        public SimulationOperationCode Code { get; }
        public IReadOnlyList<int> ConstantReferences => m_ConstantReferences;
        public int Integer0 { get; }
        public int Integer1 { get; }
        public ulong Unsigned0 { get; }
        public FixedScalar Scalar0 { get; }
        public string Text0 { get; }
        public uint Flags { get; }

        static ReadOnlyCollection<int> ReadOnly(IEnumerable<int> source)
        {
            var values = source == null ? new List<int>() : new List<int>(source);
            return values.AsReadOnly();
        }
    }

    public sealed class SimulationOperation
    {
        readonly ReadOnlyCollection<int> m_Operands;
        readonly ReadOnlyCollection<int> m_StateSlots;

        public SimulationOperation(
            OperationHandle handle,
            SimulationOperationDefinition definition,
            IEnumerable<int> operands,
            IEnumerable<int> stateSlots)
        {
            if (!handle.IsValid)
                throw new ArgumentException("Operation handle is invalid.", nameof(handle));
            Handle = handle;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            m_Operands = ReadOnly(operands);
            m_StateSlots = ReadOnly(stateSlots);
        }

        public OperationHandle Handle { get; }
        public SimulationOperationDefinition Definition { get; }
        public int DefinitionIndex => Definition.Index;
        public SimulationOperationCode Code => Definition.Code;
        public IReadOnlyList<int> Operands => m_Operands;
        public IReadOnlyList<int> ConstantReferences => Definition.ConstantReferences;
        public IReadOnlyList<int> StateSlots => m_StateSlots;
        public int Integer0 => Definition.Integer0;
        public int Integer1 => Definition.Integer1;
        public ulong Unsigned0 => Definition.Unsigned0;
        public FixedScalar Scalar0 => Definition.Scalar0;
        public string Text0 => Definition.Text0;
        public uint Flags => Definition.Flags;

        static ReadOnlyCollection<int> ReadOnly(IEnumerable<int> source)
        {
            var values = source == null ? new List<int>() : new List<int>(source);
            return values.AsReadOnly();
        }
    }
}
