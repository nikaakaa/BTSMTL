using System;
using System.Buffers;
using MemoryPack;

namespace ThirdPersonSimulation.Serialization
{
    public static partial class MemoryPackContent
    {
        public static void SerializeSnapshotInto<TBufferWriter>(IValueSnapshotCopy snapshot,
            TBufferWriter destination, MemoryPackContentContext context)
            where TBufferWriter : class, IBufferWriter<byte>
        {
            context.Part = MemoryPackContentPart.Full;
            MemoryPackSerializer.Serialize(snapshot.GetType(), in destination, snapshot, context.Options);
        }

        public static void DeserializeSnapshotInto(ReadOnlySpan<byte> bytes, IValueSnapshotCopy snapshot,
            MemoryPackContentContext context)
        {
            context.BeginRead(snapshot);
            object value = snapshot;
            int consumed = MemoryPackSerializer.Deserialize(snapshot.GetType(), bytes, ref value, context.Options);
            if (consumed != bytes.Length)
                throw new System.IO.InvalidDataException("MemoryPack content contains trailing data.");
        }
    }
}
