namespace ThirdPersonSimulation.Configuration
{
    public static class BlackboardDefaultValues
    {
        public static ulong UInt64(long bits) => unchecked((ulong)bits);
        public static string Text(bool isNull, int[] codeUnits)
        {
            if (isNull)
                return default;
            var characters = new char[codeUnits.Length];
            for (int index = 0; index < characters.Length; index++)
                characters[index] = (char)codeUnits[index];
            return new string(characters);
        }
    }
}
