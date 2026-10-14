namespace ThirdPersonSimulation
{
    internal readonly struct SimulationBlackboardSlotGroup
    {
        public SimulationBlackboardSlotGroup(int value, int ownerToken, int lifetime, int writeStamp,
            ProgramScopeLayout scope, int compiledOwnerIndex, ProgramBlackboardLifetime lifetimeKind,
            ProgramCatalogEntry declaration)
        {
            Value = value;
            OwnerToken = ownerToken;
            Lifetime = lifetime;
            WriteStamp = writeStamp;
            Scope = scope;
            CompiledOwnerIndex = compiledOwnerIndex;
            LifetimeKind = lifetimeKind;
            Declaration = declaration;
        }

        public int Value { get; }
        public int OwnerToken { get; }
        public int Lifetime { get; }
        public int WriteStamp { get; }
        public ProgramScopeLayout Scope { get; }
        public int CompiledOwnerIndex { get; }
        public ProgramBlackboardLifetime LifetimeKind { get; }
        public ProgramCatalogEntry Declaration { get; }
    }
}
