using System;
using System.IO;

namespace ThirdPersonSimulation
{
    internal static class SimulationNumericProfileCodec
    {
        public static void Write(CanonicalWriter writer, SimulationNumericProfile profile)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (!profile.IsValid)
                throw new ArgumentException("Simulation numeric profile is invalid.", nameof(profile));
            writer.WriteText(profile.Id.Value);
            writer.WriteInt32(profile.AbiVersion.Value);
            writer.WriteInt32(profile.ScalarBits);
            writer.WriteByte((byte)profile.Rounding);
            writer.WriteByte((byte)profile.Overflow);
            writer.WriteBoolean(profile.DeterministicReplay);
        }

        public static SimulationNumericProfile Read(CanonicalReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            return new SimulationNumericProfile(
                new NumericProfileId(CanonicalTextEncoding.DecodeString(reader.ReadTextSegment())),
                new TargetAbiVersion(reader.ReadInt32()),
                reader.ReadInt32(),
                ReadRounding(reader.ReadByte()),
                ReadOverflow(reader.ReadByte()),
                reader.ReadBoolean());
        }

        public static SimulationNumericProfile Read(CanonicalReader reader, in SimulationNumericProfile publishedProfile)
        {
            reader.RequireText(publishedProfile.Id.Value);
            int abiVersion = reader.ReadInt32();
            int scalarBits = reader.ReadInt32();
            var rounding = ReadRounding(reader.ReadByte());
            var overflow = ReadOverflow(reader.ReadByte());
            bool deterministicReplay = reader.ReadBoolean();
            if (abiVersion != publishedProfile.AbiVersion.Value || scalarBits != publishedProfile.ScalarBits ||
                rounding != publishedProfile.Rounding || overflow != publishedProfile.Overflow ||
                deterministicReplay != publishedProfile.DeterministicReplay)
                throw new InvalidDataException("Simulation numeric profile differs from its published profile.");
            return publishedProfile;
        }

        public static void Skip(CanonicalReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            ArraySegment<byte> identity = reader.ReadTextSegment();
            if (identity.Count == 0)
                throw new InvalidDataException("Simulation numeric profile identity is invalid.");
            new TargetAbiVersion(reader.ReadInt32());
            reader.ReadInt32();
            ReadRounding(reader.ReadByte());
            ReadOverflow(reader.ReadByte());
            reader.ReadBoolean();
        }

        static SimulationNumericRoundingMode ReadRounding(byte value)
        {
            var candidate = (SimulationNumericRoundingMode)value;
            if (candidate != SimulationNumericRoundingMode.Ieee754NearestEven &&
                candidate != SimulationNumericRoundingMode.FixedNearestEven)
                throw new InvalidDataException($"Simulation numeric profile enum '{nameof(SimulationNumericRoundingMode)}' value '{value}' is invalid.");
            return candidate;
        }

        static SimulationNumericOverflowMode ReadOverflow(byte value)
        {
            var candidate = (SimulationNumericOverflowMode)value;
            if (candidate != SimulationNumericOverflowMode.RejectNonFinite &&
                candidate != SimulationNumericOverflowMode.RejectOverflow)
                throw new InvalidDataException($"Simulation numeric profile enum '{nameof(SimulationNumericOverflowMode)}' value '{value}' is invalid.");
            return candidate;
        }
    }
}
