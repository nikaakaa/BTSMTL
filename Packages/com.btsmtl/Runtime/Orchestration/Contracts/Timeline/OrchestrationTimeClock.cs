using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationTimeClock : IOrchestrationTimelineClock<OrchestrationTime>
    {
        public OrchestrationTime Zero => OrchestrationTime.Zero;
        public OrchestrationTime One => OrchestrationTime.One;
        public long Raw(OrchestrationTime value) => value.RawValue;
        public OrchestrationTime FromRaw(long value) => OrchestrationTime.FromRaw(value);
        public OrchestrationTime FromInt32(int value) => OrchestrationTime.FromInt32(value);
        public OrchestrationTime FromInt64(long value) => OrchestrationTime.FromInt64(value);
        public int FloorToInt32(OrchestrationTime value) =>
            unchecked((int)(value.RawValue >> OrchestrationTime.Precision));
        public OrchestrationTime Add(OrchestrationTime left, OrchestrationTime right) => left + right;
        public OrchestrationTime Subtract(OrchestrationTime left, OrchestrationTime right) => left - right;
        public OrchestrationTime Multiply(OrchestrationTime left, OrchestrationTime right) => left * right;
        public OrchestrationTime Divide(OrchestrationTime left, OrchestrationTime right) => left / right;
        public OrchestrationTime Clamp(OrchestrationTime value,
            OrchestrationTime minimum, OrchestrationTime maximum) =>
            OrchestrationTime.Clamp(value, minimum, maximum);
        public OrchestrationTime Negate(OrchestrationTime value) => -value;
        public OrchestrationTime Max(OrchestrationTime left, OrchestrationTime right) =>
            OrchestrationTime.Max(left, right);
        public OrchestrationTime Min(OrchestrationTime left, OrchestrationTime right) =>
            OrchestrationTime.Min(left, right);
        public OrchestrationTime Abs(OrchestrationTime value)
        {
            long sign = value.RawValue >> 63;
            return OrchestrationTime.FromRaw(unchecked((value.RawValue + sign) ^ sign));
        }
        public bool Equal(OrchestrationTime left, OrchestrationTime right) => left == right;
        public bool LessThanOrEqual(OrchestrationTime left, OrchestrationTime right) => left <= right;
        public bool GreaterThan(OrchestrationTime left, OrchestrationTime right) => left > right;
        public bool IsZero(OrchestrationTime value) => value.RawValue == 0;
    }
}
