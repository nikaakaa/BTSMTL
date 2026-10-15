namespace ThirdPersonSimulation.Collision
{
    public readonly struct SweepQuerySource
    {
        public SweepQuerySource(ulong requestId, ActorId actor, SimulationTick tick, TimelineActionContextIdentity actionContext, string contentId, CanonicalTextValue nodeId, ulong playbackId, ulong callId)
        {
            RequestId = requestId;
            Actor = actor;
            Tick = tick;
            ActionContext = actionContext;
            ContentId = contentId;
            NodeId = nodeId;
            PlaybackId = playbackId;
            CallId = callId;
        }
        public ulong RequestId { get; }
        public ActorId Actor { get; }
        public SimulationTick Tick { get; }
        public TimelineActionContextIdentity ActionContext { get; }
        public string ContentId { get; }
        public CanonicalTextValue NodeId { get; }
        public ulong PlaybackId { get; }
        public ulong CallId { get; }
    }
}
