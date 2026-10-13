using ThirdPersonSimulation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation.Fixed
{
    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class ProgramConstant
    {
        public int Index { get; }
        public string Identity { get; private set; }
        [ValueUnionCase] public bool Boolean { get; }
        [ValueUnionCase] public int Int32 { get; }
        [ValueUnionCase] public ulong UInt64 { get; }
        [ValueUnionCase] public FixedScalar Scalar { get; }
        [ValueUnionCase] public FixedVector2 Vector2 { get; }
        [ValueUnionCase] public FixedVector3 Vector3 { get; }
        [ValueUnionCase] public FixedYaw Yaw { get; }
        [ValueUnionCase] public string String { get; private set; }
        [ValueUnionCase] public ReadOnlyMemory<byte> Bytes { get; }
        public string Text => String;

        [CanonicalValidation]
        void RequireRecord()
        {
            if (Index < 0)
                throw new ArgumentOutOfRangeException(nameof(Index));
            Identity = SimulationIdentity.Require(Identity, nameof(Identity));
            String = String ?? string.Empty;
        }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public readonly partial struct SimulationOperationParameterValues
    {
        public SimulationOperationParameterValues(
            int integer0,
            int integer1,
            ulong unsigned0,
            FixedScalar scalar0,
            string text0,
            uint flags)
        {
            Integer0 = integer0;
            Integer1 = integer1;
            Unsigned0 = unsigned0;
            Scalar0 = scalar0;
            Text0 = text0;
            Flags = flags;
        }

        public int Integer0 { get; }
        public int Integer1 { get; }
        public ulong Unsigned0 { get; }
        public FixedScalar Scalar0 { get; }
        public string Text0 { get; }
        public uint Flags { get; }
    }

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    public sealed partial class SimulationOperationDefinition
    {
        readonly ReadOnlyCollection<int> m_ConstantReferences;

        public SimulationOperationDefinition(
            int index,
            string identity,
            SimulationOperationCode code,
            IEnumerable<int> constantReferences)
        {
            Index = index;
            Identity = identity.Trim();
            Code = code;
            m_ConstantReferences = ReadOnly(constantReferences);
            RequireRecord();
        }

        public int Index { get; }
        public string Identity { get; }
        public SimulationOperationCode Code { get; }
        public IReadOnlyList<int> ConstantReferences => m_ConstantReferences;

        [CanonicalValidation]
        void RequireRecord()
        {
            if (Index < 0)
                throw new ArgumentOutOfRangeException(nameof(Index));
            SimulationIdentity.Require(Identity, nameof(Identity));
        }
        static ReadOnlyCollection<int> ReadOnly(IEnumerable<int> source)
        {
            var values = source == null ? new List<int>() : new List<int>(source);
            return values.AsReadOnly();
        }
    }

    public sealed class SimulationOperation
    {
        readonly SimulationOperationParameterValues[] m_Parameters;

        public SimulationOperation(OperationHandle handle, SimulationOperationDefinition definition,
            IEnumerable<int> operands, IEnumerable<int> stateSlots,
            SimulationOperationParameterValues[] parameters, int parameterIndex)
            : this(new ProgramOperationLayout(handle, definition.Index, parameterIndex, operands, stateSlots),
                definition, parameters)
        {
        }

        internal SimulationOperation(ProgramOperationLayout layout, SimulationOperationDefinition definition,
            SimulationOperationParameterValues[] parameters)
        {
            Layout = layout;
            Definition = definition;
            m_Parameters = parameters;
        }

        internal SimulationOperation(SimulationOperation source, SimulationOperationParameterValues[] parameters)
            : this(source.Layout, source.Definition, parameters)
        {
        }

        public ProgramOperationLayout Layout { get; }
        public OperationHandle Handle => Layout.Handle;
        public SimulationOperationDefinition Definition { get; }
        public int DefinitionIndex => Layout.DefinitionIndex;
        public int ParameterIndex => Layout.ParameterIndex;
        internal SimulationOperationParameterValues[] ParameterValues => m_Parameters;
        public SimulationOperationCode Code => Definition.Code;
        public IReadOnlyList<int> Operands => Layout.Operands;
        public IReadOnlyList<int> ConstantReferences => Definition.ConstantReferences;
        public IReadOnlyList<int> StateSlots => Layout.StateSlots;
        public int Integer0 => m_Parameters[ParameterIndex].Integer0;
        public int Integer1 => m_Parameters[ParameterIndex].Integer1;
        public ulong Unsigned0 => m_Parameters[ParameterIndex].Unsigned0;
        public FixedScalar Scalar0 => m_Parameters[ParameterIndex].Scalar0;
        public string Text0 => m_Parameters[ParameterIndex].Text0;
        public uint Flags => m_Parameters[ParameterIndex].Flags;
    }
}
