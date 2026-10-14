using System;

namespace ThirdPersonSimulation
{
    public sealed class SimulationCharacterRuntimeDescriptor
    {
        public SimulationCharacterRuntimeDescriptor(
            SimulationExecutionTargetManifest executionTarget,
            GameplayContentHash gameplayContentHash,
            StableHash stateSchemaHash,
            SimulationActorRosterDescriptor roster)
        {
            ExecutionTarget = executionTarget ?? throw new ArgumentNullException(nameof(executionTarget));
            if (!gameplayContentHash.IsValid || !stateSchemaHash.IsValid)
                throw new ArgumentException("Character Runtime content identity is invalid.");
            Roster = roster ?? throw new ArgumentNullException(nameof(roster));
            GameplayContentHash = gameplayContentHash;
            StateSchemaHash = stateSchemaHash;
            Identity = StableHash.Compute(
                "simulation-character-runtime-descriptor/2",
                ExecutionTarget.Identity.ToString(),
                GameplayContentHash.ToString(),
                StateSchemaHash.ToString(),
                Roster.RosterHash.ToString());
        }

        public SimulationExecutionTargetManifest ExecutionTarget { get; }
        public SimulationNumericProfile NumericProfile => ExecutionTarget.NumericProfile;
        public NumericProfileId NumericProfileId => NumericProfile.Id;
        public TargetAbiVersion TargetAbiVersion => NumericProfile.AbiVersion;
        public OperationSetVersion OperationSetVersion => ExecutionTarget.OperationSetVersion;
        public GameplayContentHash GameplayContentHash { get; }
        public StableHash StateSchemaHash { get; }
        public SimulationActorRosterDescriptor Roster { get; }
        public StableHash Identity { get; }
    }
}
