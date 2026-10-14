using System;
using System.IO;
using ThirdPersonSimulation.FPGameplay;

namespace ThirdPersonSimulation.Serialization
{
    public static class MemoryPackInputTraceContent
    {
        public static FPCharacterInputTrace ReadValidatedTrace(FixedInputTraceDocument document)
        {
            var actor = new ActorId(document.actor_id);
            var frames = new FPCharacterInputTraceFrame[document.frames.Length];
            for (int index = 0; index < frames.Length; index++)
            {
                FixedInputTraceFrameDocument source = document.frames[index];
                frames[index] = MemoryPackContent.Deserialize<FPCharacterInputTraceFrame>(
                    Convert.FromBase64String(source.input_payload_base64));
                if (frames[index].Tick.Value != source.simulation_tick)
                    throw new InvalidDataException("FP input trace frame Tick does not match its document.");
            }
            return new FPCharacterInputTrace(document.trace_id, actor, document.tick_rate, frames);
        }
    }
}
