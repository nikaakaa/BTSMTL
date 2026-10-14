using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using ThirdPersonSimulation;
using System;
using System.Collections.Generic;
using SimulationOperationParameterValues = ThirdPersonSimulation.SimulationOperationParameterValues<Photon.Deterministic.FP>;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed partial class ProgramConstant
    {
        public int Index { get; }
        public string Identity { get; private set; }
        [ValueUnionCase] public bool Boolean { get; }
        [ValueUnionCase] public int Int32 { get; }
        [ValueUnionCase] public ulong UInt64 { get; }
        [ValueUnionCase] public FP Scalar { get; }
        [ValueUnionCase] public FPVector2 Vector2 { get; }
        [ValueUnionCase] public FPVector3 Vector3 { get; }
        [ValueUnionCase] public FP Yaw { get; }
        [ValueUnionCase] public string String { get; private set; }
        [ValueUnionCase] public GameplayAbilityCurve<FP> Curve { get; }
        public string Text => String;

        [ValueValidation]
        void RequireRecord()
        {
            if (Index < 0)
                throw new ArgumentOutOfRangeException(nameof(Index));
            Identity = SimulationIdentity.Require(Identity, nameof(Identity));
            String = String ?? string.Empty;
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
        public FP Scalar0 => m_Parameters[ParameterIndex].Scalar0;
        public string Text0 => m_Parameters[ParameterIndex].Text0;
        public uint Flags => m_Parameters[ParameterIndex].Flags;
    }
}
