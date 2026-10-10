using System;

namespace ThirdPersonSimulation
{
    [CanonicalContract]
    public partial struct SimulationInputHeader
    {
        public SimulationInputHeader(SimulationTickSourceIdentity tickSource, CanonicalTextValue inputSourceIdentity, ulong sequence)
        {
            TickSource = tickSource;
            InputSourceIdentity = inputSourceIdentity;
            Sequence = sequence;
            RequireValid();
        }

        public SimulationTickSourceIdentity TickSource { get; private set; }
        public CanonicalTextValue InputSourceIdentity { get; }
        public ulong Sequence { get; }

        public SimulationInputHeader BindStepSource(SimulationTickSourceIdentity source)
        {
            SimulationInputHeader result = this;
            result.TickSource = source;
            return result;
        }

        [CanonicalValidation]
        void RequireValid()
        {
            SimulationIdentity.Require(InputSourceIdentity, nameof(InputSourceIdentity));
            if (Sequence == 0 || TickSource.Kind is not
                (SimulationTickSourceKind.LocalLogic or SimulationTickSourceKind.Authoritative or SimulationTickSourceKind.Replay))
                throw new ArgumentException("Simulation input header is invalid.");
        }
    }
}
