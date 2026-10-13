using System;
using System.IO;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.ServerAuthoritative
{
    public sealed class ServerAuthoritativeAuthoritySourcePolicy
    {
        public ServerAuthoritativeAuthoritySourcePolicy(
            ServerAuthoritativeModelPolicy modelPolicy,
            int commandQueueCapacity,
            int reliableOutputQueueCapacity,
            int fullCheckpointOutputQueueCapacity,
            int commandLivenessTimeoutTicks,
            int controlHeartbeatTicks,
            int maxCatchUpTicksPerPump,
            int maxClockLagTicks)
        {
            ModelPolicy = modelPolicy ?? throw new ArgumentNullException(nameof(modelPolicy));
            if (commandQueueCapacity <= 0 || reliableOutputQueueCapacity <= 0 ||
                fullCheckpointOutputQueueCapacity <= 0 || commandLivenessTimeoutTicks <= 0 ||
                controlHeartbeatTicks <= 0 || maxCatchUpTicksPerPump <= 0 ||
                maxClockLagTicks < maxCatchUpTicksPerPump)
            {
                throw new ArgumentException("ServerAuthoritative Authority Source policy is incomplete.");
            }
            CommandQueueCapacity = commandQueueCapacity;
            ReliableOutputQueueCapacity = reliableOutputQueueCapacity;
            FullCheckpointOutputQueueCapacity = fullCheckpointOutputQueueCapacity;
            CommandLivenessTimeoutTicks = commandLivenessTimeoutTicks;
            ControlHeartbeatTicks = controlHeartbeatTicks;
            MaxCatchUpTicksPerPump = maxCatchUpTicksPerPump;
            MaxClockLagTicks = maxClockLagTicks;
            ConfigurationHash = SimulationCanonicalPayloadHash.Compute(
                ServerAuthoritativeAuthoritySourcePolicyCodec.Write(this));
        }

        public ServerAuthoritativeModelPolicy ModelPolicy { get; }
        public int CommandQueueCapacity { get; }
        public int ReliableOutputQueueCapacity { get; }
        public int FullCheckpointOutputQueueCapacity { get; }
        public int CommandLivenessTimeoutTicks { get; }
        public int ControlHeartbeatTicks { get; }
        public int MaxCatchUpTicksPerPump { get; }
        public int MaxClockLagTicks { get; }
        public StableHash ConfigurationHash { get; }
    }

    public static class ServerAuthoritativeAuthoritySourcePolicyCodec
    {
        const int Magic = 0x53415350;
        const int SchemaVersion = 2;

        public static byte[] Write(ServerAuthoritativeAuthoritySourcePolicy policy)
        {
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            ServerAuthoritativeModelPolicy model = policy.ModelPolicy;
            using var writer = new CanonicalWriter();
            writer.WriteInt32(Magic);
            writer.WriteInt32(SchemaVersion);
            writer.WriteInt32(model.SimulationTickRate);
            writer.WriteInt32(model.CommandPacketRate);
            writer.WriteInt32(model.SnapshotPacketRate);
            writer.WriteInt32(model.CommandSlackTicks);
            writer.WriteInt32(model.MaximumRemoteBodyExtrapolationTicks);
            writer.WriteInt32(model.MaxGameplayDatagramBytes);
            writer.WriteInt32(model.HistoryCapacity);
            writer.WriteInt32(model.MaximumInputLeadTicks);
            writer.WriteInt32(model.MaximumInputLagTicks);
            writer.WriteInt32(model.MaximumReplayTicksPerOuterTick);
            writer.WriteInt32(BitConverter.SingleToInt32Bits(model.BodyPositionTolerance));
            writer.WriteInt32(BitConverter.SingleToInt32Bits(model.BodyYawToleranceDegrees));
            writer.WriteByte((byte)model.HardRecoveryPolicy);
            writer.WriteByte((byte)model.MissingInputPolicy);
            writer.WriteInt32(policy.CommandQueueCapacity);
            writer.WriteInt32(policy.ReliableOutputQueueCapacity);
            writer.WriteInt32(policy.FullCheckpointOutputQueueCapacity);
            writer.WriteInt32(policy.CommandLivenessTimeoutTicks);
            writer.WriteInt32(policy.ControlHeartbeatTicks);
            writer.WriteInt32(policy.MaxCatchUpTicksPerPump);
            writer.WriteInt32(policy.MaxClockLagTicks);
            return writer.ToArray();
        }

        public static ServerAuthoritativeAuthoritySourcePolicy Read(byte[] bytes)
        {
            var reader = new CanonicalReader(bytes ?? throw new ArgumentNullException(nameof(bytes)));
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != SchemaVersion)
                throw new InvalidDataException("Authority Source policy identity is invalid.");
            var model = new ServerAuthoritativeModelPolicy(
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                BitConverter.Int32BitsToSingle(reader.ReadInt32()),
                BitConverter.Int32BitsToSingle(reader.ReadInt32()),
                (ServerAuthoritativeHardRecoveryPolicy)reader.ReadByte(),
                (ServerAuthoritativeMissingInputPolicy)reader.ReadByte());
            var policy = new ServerAuthoritativeAuthoritySourcePolicy(
                model,
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadInt32());
            reader.RequireComplete();
            if (!SimulationCanonicalPayloadHash.Compute(bytes).Equals(policy.ConfigurationHash))
                throw new InvalidDataException("Authority Source policy hash is invalid.");
            return policy;
        }
    }
}
