namespace ThirdPersonSimulation
{
    public readonly struct RecordedFrameSlice
    {
        public RecordedFrameSlice(long offset, int length)
        {
            Offset = offset;
            Length = length;
        }

        public long Offset { get; }
        public int Length { get; }
    }
}
