using BTSMTL.Timeline;
using MemoryPack;
using System.IO;

namespace ThirdPersonSimulation.Serialization
{
    sealed class TimelineContentParametersFormatter : MemoryPackFormatter<TimelineContentParameters>
    {
#if NET7_0_OR_GREATER
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer,
            scoped ref TimelineContentParameters value)
#else
        public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer,
            ref TimelineContentParameters value)
            where TBufferWriter : class
#endif
        {
            var codecs = (TimelineContentParameterCodecs)writer.Options.ServiceProvider.GetService(
                typeof(TimelineContentParameterCodecs));
            string contractKind = value.ContractKind;
            ITimelineContentParameterCodec codec = codecs.Require(contractKind);
            writer.WriteValue(contractKind);
            writer.WriteValue(codec.SchemaIdentity.Value);
            writer.WriteValue(codec.Write(value));
        }

#if NET7_0_OR_GREATER
        public override void Deserialize(ref MemoryPackReader reader, scoped ref TimelineContentParameters value)
#else
        public override void Deserialize(ref MemoryPackReader reader, ref TimelineContentParameters value)
#endif
        {
            string contractKind = reader.ReadValue<string>();
            var codecs = (TimelineContentParameterCodecs)reader.Options.ServiceProvider.GetService(
                typeof(TimelineContentParameterCodecs));
            ITimelineContentParameterCodec codec = codecs.Require(contractKind);
            if (reader.ReadValue<string>() != codec.SchemaIdentity.Value)
                throw new InvalidDataException($"Timeline 参数 '{contractKind}' 的生成 schema 不匹配，请沿正式入口重新发布。");
            value = codec.Read(reader.ReadValue<byte[]>());
        }
    }
}
