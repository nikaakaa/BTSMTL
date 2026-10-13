namespace ThirdPersonSimulation
{
    [CanonicalContract]
    internal readonly partial struct AbilityInputEvent
    {
        internal AbilityInputEvent(byte kind, ActorId sourceActor, ActorId targetActor, ulong sourceInstance,
            ulong targetInstance, CanonicalTextValue eventId, CanonicalTextValue attackEvent,
            CanonicalTextValue sourceFeedback, CanonicalTextValue targetFeedback, byte disposition)
        {
            Kind = kind;
            SourceActor = sourceActor;
            TargetActor = targetActor;
            SourceInstance = sourceInstance;
            TargetInstance = targetInstance;
            EventId = eventId;
            AttackEvent = attackEvent;
            SourceFeedback = sourceFeedback;
            TargetFeedback = targetFeedback;
            Disposition = disposition;
        }

        internal readonly byte Kind, Disposition;
        internal readonly ActorId SourceActor, TargetActor;
        internal readonly ulong SourceInstance, TargetInstance;
        internal readonly CanonicalTextValue EventId, AttackEvent, SourceFeedback, TargetFeedback;
    }
}
