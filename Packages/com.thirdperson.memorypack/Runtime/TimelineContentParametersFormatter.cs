using BTSMTL.Timeline;
using MemoryPack;

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
            writer.WriteValue(contractKind);
            writer.WriteValue(codecs.Require(contractKind).Write(value));
        }

#if NET7_0_OR_GREATER
        public override void Deserialize(ref MemoryPackReader reader, scoped ref TimelineContentParameters value)
#else
        public override void Deserialize(ref MemoryPackReader reader, ref TimelineContentParameters value)
#endif
        {
            string contractKind = reader.ReadValue<string>();
            byte[] bytes = reader.ReadValue<byte[]>();
            var codecs = (TimelineContentParameterCodecs)reader.Options.ServiceProvider.GetService(
                typeof(TimelineContentParameterCodecs));
            value = codecs.Require(contractKind).Read(bytes);
        }
    }
}
