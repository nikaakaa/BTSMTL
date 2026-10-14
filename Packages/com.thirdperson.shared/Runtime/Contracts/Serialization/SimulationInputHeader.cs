using System;

namespace ThirdPersonSimulation
{
    [ValueOperations]
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
        public CanonicalTextValue InputSourceIdentity { get; private set; }
        public ulong Sequence { get; private set; }

        public SimulationInputHeader BindStepSource(SimulationTickSourceIdentity source)
        {
            SimulationInputHeader result = this;
            result.TickSource = source;
            return result;
        }

        [ValueValidation]
        void RequireValid()
        {
            SimulationIdentity.Require(InputSourceIdentity, nameof(InputSourceIdentity));
            if (Sequence == 0 || TickSource.Kind is not
                (SimulationTickSourceKind.LocalLogic or SimulationTickSourceKind.Authoritative or SimulationTickSourceKind.Replay))
                throw new ArgumentException("Simulation input header is invalid.");
        }
    }
}
