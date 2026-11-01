namespace ThirdPersonSimulation.Serialization
{
    // 生成的 MemoryPack 格式器实现此接口；不依赖 MemoryPack，中立程序集与宿主共用同一 schema 身份。
    public interface IMemoryPackSchema
    {
        StableHash SchemaHash { get; }
    }
}
