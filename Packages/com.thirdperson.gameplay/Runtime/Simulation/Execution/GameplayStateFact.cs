namespace ThirdPersonSimulation
{
    [CanonicalContract]
    public readonly partial struct GameplayStateFact<TScalar>
        where TScalar : struct, ICanonicalRecord<TScalar>
    {
        public GameplayStateFact(CanonicalTextValue subjectId, CanonicalTextValue stateId, TScalar scalar)
        {
            SubjectId = SimulationIdentity.Require(subjectId, nameof(subjectId));
            StateId = stateId.IsNull ? CanonicalTextValue.Empty : stateId;
            Scalar = scalar;
        }

        [CanonicalTextRules(Required = true, Trim = true)]
        public CanonicalTextValue SubjectId { get; }
        [CanonicalTextRules(NullToEmpty = true)]
        public CanonicalTextValue StateId { get; }
        public TScalar Scalar { get; }
    }
    [CanonicalContract]
    public readonly partial struct GameplayEffectReconciliationFact<TContext>
        where TContext : struct, ICanonicalRecord<TContext>
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
