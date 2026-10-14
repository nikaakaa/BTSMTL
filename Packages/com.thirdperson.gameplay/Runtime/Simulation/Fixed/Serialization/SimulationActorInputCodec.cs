using System;
using System.IO;
using ThirdPersonSimulation.DeterministicRollback;

namespace ThirdPersonSimulation.Fixed
{
    public static class SimulationActorInputCodec
    {
        const uint Magic = 0x49524244;
        public const int Version = 6;
        public const int MinimumEncodedSize = sizeof(uint) + sizeof(int) + sizeof(int) + 64 + sizeof(byte) +
            RollbackActorInputHeader.CanonicalMinimumSize + SimulationInputCodec.MinimumEncodedSize;

        public static void Write<TWriter>(TWriter writer, in RollbackActorInputHeader header,
            SimulationInput input, RollbackInputProvenance provenance) where TWriter : class, ICanonicalValueWriter
        {
            writer.WriteUInt32(Magic);
            writer.WriteInt32(Version);
            writer.WriteHash(RollbackActorInputHeader.CanonicalSchemaHash);
            writer.WriteByte((byte)provenance);
            WritePayload(writer, in header, input);
        }

        public static void WritePayload<TWriter>(TWriter writer, in RollbackActorInputHeader header,
            SimulationInput input) where TWriter : class, ICanonicalValueWriter
        {
            RollbackActorInputHeader.CanonicalWrite(writer, in header);
            SimulationInputCodec.Write(writer, input);
        }

        public static RollbackActorInputHeader ReadHeader(CanonicalReader reader,
            out RollbackInputProvenance provenance)
        {
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != Version)
                throw new InvalidDataException("Rollback Actor input header is invalid.");
            RollbackActorInputHeader.CanonicalRequireSchema(reader);
            provenance = (RollbackInputProvenance)reader.ReadByte();
            return RollbackActorInputHeader.CanonicalRead(reader);
        }

        public static void WriteLocalFrame(CanonicalWriter writer, in FixedCharacterInputTraceFrame frame)
        {
            var header = new RollbackActorInputHeader(frame.ActorId,
                new SimulationTick(frame.Input.TickSource.SourceTick));
            Write(writer, in header, frame.Input, RollbackInputProvenance.LocalExplicit);
        }

        public static FixedCharacterInputTraceFrame ReadLocalFrame(byte[] bytes, ActorId actor,
            SimulationTick simulationTick)
        {
            var reader = new CanonicalReader(bytes);
            reader.SetTextResolver(CanonicalInitializationTextResolver.Instance);
            RollbackActorInputHeader header = ReadHeader(reader, out RollbackInputProvenance provenance);
            SimulationInput input = SimulationInputCodec.Read(reader);
            try
            {
                reader.RequireComplete();
                if (provenance != RollbackInputProvenance.LocalExplicit || header.ActorId != actor ||
                    input.TickSource.SourceTick != header.Tick.Value)
                    throw new InvalidDataException("Canonical Fixed input trace frame has an invalid identity.");
                return new FixedCharacterInputTraceFrame(header.ActorId, simulationTick, input);
            }
            finally { input.ReleaseText(); }
        }

        public static FixedCharacterInputTrace ReadTrace(FixedInputTraceDocument document)
        {
            if (document.schema != FixedInputTraceDocument.CurrentSchema)
                throw new InvalidDataException($"固定输入版本为 {document.schema}，当前需要 {FixedInputTraceDocument.CurrentSchema}。请使用离线 FixedInputTraceMigration 迁移原录制。");
            var frames = new FixedCharacterInputTraceFrame[document.frames.Length];
            var actor = new ActorId(document.actor_id);
            for (int i = 0; i < frames.Length; i++)
                frames[i] = ReadLocalFrame(Convert.FromBase64String(document.frames[i].input_payload_base64),
                    actor, new SimulationTick(document.frames[i].simulation_tick));
            return new FixedCharacterInputTrace(document.trace_id, actor, document.tick_rate, frames);
        }
    }
}
