using BTSMTL.Timeline;

namespace ThirdPersonSimulation.Serialization
{
    public static class MovementTimelineContentCodecs
    {
        static MovementTimelineContentCodecs()
        {
            MemoryPackContent.Initialize();
            MemoryPackGeneratedFormatters.Register();
            Motion = new TimelineContentParameterCodec<TimelineContentMotion>(TimelineContentMotion.Kind,
                MemoryPackContent.GetSchemaHash<TimelineContentMotion>(),
                value => MemoryPackContent.Serialize(value),
                bytes => MemoryPackContent.Deserialize<TimelineContentMotion>(bytes));
        }

        public static TimelineContentParameterCodec<TimelineContentMotion> Motion { get; }
    }
}
