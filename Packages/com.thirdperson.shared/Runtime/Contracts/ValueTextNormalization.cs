namespace ThirdPersonSimulation
{
    public static class ValueTextNormalization
    {
        public static string Apply(string value, bool required, bool trim, bool nullToEmpty,
            bool encoded, string member)
        {
            string original = value;
            if (required)
                value = SimulationIdentity.Require(value, member);
            else
            {
                if (nullToEmpty && value == null)
                    value = string.Empty;
                if (trim && value != null)
                    value = value.Trim();
            }
            if (encoded && ((original == null) != (value == null) || original?.Length != value?.Length))
                throw new System.IO.InvalidDataException("Canonical text is not normalized: " + member);
            return value;
        }
    }
}
