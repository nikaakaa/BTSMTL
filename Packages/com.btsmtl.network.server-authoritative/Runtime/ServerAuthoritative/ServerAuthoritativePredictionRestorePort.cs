using System;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public sealed class ServerAuthoritativePredictionRestorePort : IServerAuthoritativePredictionRestorePort
    {
        string m_SnapshotId;
        Float32SimulationSessionSnapshot m_Snapshot;

        public ServerAuthoritativePredictionRestorePort(SimulationComponentIdentity source)
        {
            Descriptor = SimulationPortDescriptor.CreateSource(
                ServerAuthoritativeSourcePortContracts.PredictionRestore,
                source);
        }

        public SimulationPortDescriptor Descriptor { get; }

        public void Store(string snapshotId, Float32SimulationSessionSnapshot snapshot)
        {
            if (CanonicalTextValue.IsNullOrWhiteSpace(snapshotId))
                throw new ArgumentException("Prediction restore snapshot id is required.", nameof(snapshotId));
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            m_SnapshotId = snapshotId;
            m_Snapshot = snapshot;
        }

        public void Clear()
        {
            m_SnapshotId = null;
            m_Snapshot = null;
        }

        public Float32SimulationSessionSnapshot GetRequiredSnapshot(SimulationRestoreDirective directive)
        {
            if (directive == null)
                throw new ArgumentNullException(nameof(directive));
            if (!CanonicalTextValue.Equals(m_SnapshotId, directive.SnapshotId, StringComparison.Ordinal) || m_Snapshot == null)
                throw new InvalidOperationException($"Prediction restore snapshot '{directive.SnapshotId}' is unavailable.");
            Float32SimulationSessionSnapshot snapshot = m_Snapshot;
            if (snapshot.Tick != directive.Tick || snapshot.SnapshotHash != directive.SnapshotHash)
                throw new InvalidOperationException($"Prediction restore snapshot '{directive.SnapshotId}' does not match its directive.");
            Clear();
            return snapshot;
        }
    }
}
