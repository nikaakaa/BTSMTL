namespace ThirdPersonSimulation.Fixed
{
    public static class SimulationInputCodec
    {
        public const string Schema = SimulationInputHeader.CanonicalSchema + ":" + SimulationInputValues.CanonicalSchema;

        public static int Measure(SimulationInput input) =>
            2 * (sizeof(int) + 64) + SimulationInputHeader.CanonicalMeasure(input.Header) +
            SimulationInputValues.CanonicalMeasure(input.CanonicalValues);

        public static void Write(CanonicalWriter writer, SimulationInput input)
        {
            writer.WriteHash(SimulationInputHeader.CanonicalSchemaHash);
            SimulationInputHeader.CanonicalWrite(writer, input.Header);
            WriteValuesAndRequests(writer, input);
        }

        public static void WriteValuesAndRequests(CanonicalWriter writer, SimulationInput input)
        {
            writer.WriteHash(SimulationInputValues.CanonicalSchemaHash);
            SimulationInputValues.CanonicalWrite(writer, input.CanonicalValues);
        }

        public static SimulationInput Read(CanonicalReader reader)
        {
            SimulationInputHeader header = ReadHeader(reader);
            SimulationInputValues.CanonicalRequireSchema(reader);
            SimulationInputValues data = SimulationInputValues.CanonicalReadMaterialized(reader);
            return new SimulationInput(new SimulationInputFrameStorage(in header, data), 0);
        }

        public static SimulationInput Read(CanonicalReader reader, SimulationInputFrameStorage storage)
        {
            SimulationInputHeader header = ReadHeader(reader);
            SimulationInputValues.CanonicalRequireSchema(reader);
            storage.Begin();
            SimulationInputValues.CanonicalReadInto(reader, storage.CanonicalValues);
            return storage.CompleteRead(in header);
        }

        static SimulationInputHeader ReadHeader(CanonicalReader reader)
        {
            SimulationInputHeader.CanonicalRequireSchema(reader);
            return SimulationInputHeader.CanonicalRead(reader);
        }

        public static void Skip(CanonicalReader reader)
        {
            SimulationInputHeader.CanonicalRequireSchema(reader);
            SimulationInputHeader.CanonicalSkip(reader);
            SimulationInputValues.CanonicalRequireSchema(reader);
            SimulationInputValues.CanonicalSkip(reader);
        }

        public static void SkipValue(CanonicalReader reader) => SimulationInputValue.CanonicalSkipPayload(reader);
        public static void WriteValue(CanonicalWriter writer, SimulationInputValue value) =>
            SimulationInputValue.CanonicalWrite(writer, in value);
        public static SimulationInputValue ReadValue(CanonicalReader reader, CanonicalTextValue inputId) =>
            SimulationInputValue.CanonicalReadPayload(reader, inputId);
    }
}
