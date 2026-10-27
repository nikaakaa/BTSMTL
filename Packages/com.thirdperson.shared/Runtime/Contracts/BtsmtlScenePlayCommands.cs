namespace ThirdPersonCharacter.Pipeline
{
    public enum BtsmtlScenePlayScheduledInputKind
    {
        Value,
        Request
    }

    public readonly struct BtsmtlScenePlayFrame
    {
        public BtsmtlScenePlayFrame(long frameNumber, ulong logicTick, double logicSeconds)
        {
            FrameNumber = frameNumber;
            LogicTick = logicTick;
            LogicSeconds = logicSeconds;
        }

        public long FrameNumber { get; }
        public ulong LogicTick { get; }
        public double LogicSeconds { get; }
    }

    public enum BtsmtlScenePlayCommandPhase
    {
        Pending,
        Applied,
        Rejected
    }

    public readonly struct BtsmtlScenePlayCommandResult
    {
        public BtsmtlScenePlayCommandResult(BtsmtlScenePlayCommandPhase phase, long frameNumber, string message)
        {
            Phase = phase;
            FrameNumber = frameNumber;
            Message = message;
        }

        public BtsmtlScenePlayCommandPhase Phase { get; }
        public long FrameNumber { get; }
        public string Message { get; }
    }

    public readonly struct BtsmtlScenePlayActionState
    {
        public BtsmtlScenePlayActionState(BtsmtlScenePlayCommandResult request, ulong actionInstanceId, bool running)
        {
            Request = request;
            ActionInstanceId = actionInstanceId;
            Running = running;
        }

        public BtsmtlScenePlayCommandResult Request { get; }
        public ulong ActionInstanceId { get; }
        public bool Running { get; }
    }
}
