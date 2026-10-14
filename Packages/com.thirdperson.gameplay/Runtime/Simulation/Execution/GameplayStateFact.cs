namespace ThirdPersonSimulation
{
    [ValueOperations]
    public readonly partial struct GameplayStateFact<TScalar>
        where TScalar : unmanaged
    {
        public GameplayStateFact(CanonicalTextValue subjectId, CanonicalTextValue stateId, TScalar scalar)
        {
            SubjectId = SimulationIdentity.Require(subjectId, nameof(subjectId));
            StateId = stateId.IsNull ? CanonicalTextValue.Empty : stateId;
            Scalar = scalar;
        }

        [ValueTextRules(Required = true, Trim = true)]
        public CanonicalTextValue SubjectId { get; }
        [ValueTextRules(NullToEmpty = true)]
        public CanonicalTextValue StateId { get; }
        public TScalar Scalar { get; }
    }
    [ValueOperations]
    public readonly partial struct GameplayEffectReconciliationFact<TContext>
        where TContext : struct, IValueResources
    {
        public GameplayEffectReconciliationFact(GameplayEffectReconciliationResult result, TContext context)
        {
            Result = result;
            Context = context;
        }

        public GameplayEffectReconciliationResult Result { get; }
        public TContext Context { get; }
    }
}
