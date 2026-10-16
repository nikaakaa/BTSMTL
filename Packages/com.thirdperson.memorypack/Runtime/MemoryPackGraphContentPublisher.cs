namespace ThirdPersonSimulation.Serialization
{
    public sealed partial class MemoryPackGraphContentPublisher
    {
        public static MemoryPackGraphContentPublisher Instance { get; } = new MemoryPackGraphContentPublisher();

        public byte[] WriteIdentity(GraphExecutionIdentity identity) => MemoryPackContent.Serialize(identity);

        public GraphExecutionIdentity ReadIdentity(byte[] bytes) => MemoryPackContent.Deserialize<GraphExecutionIdentity>(bytes);
    }
}
