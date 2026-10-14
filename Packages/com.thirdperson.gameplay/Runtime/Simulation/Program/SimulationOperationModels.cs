using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public readonly struct SimulationOperationParameterValues<TScalar>
        where TScalar : struct
    {
        public SimulationOperationParameterValues(
            int integer0,
            int integer1,
            ulong unsigned0,
            TScalar scalar0,
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
        public TScalar Scalar0 { get; }
        public string Text0 { get; }
        public uint Flags { get; }
    }

    public sealed class SimulationOperationDefinition
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
}
