namespace ThirdPersonSimulation
{
    public readonly struct SemanticEnumValue
    {
        public SemanticEnumValue(string typeIdentity, int value)
        {
            TypeIdentity = typeIdentity;
            Value = value;
        }

        public string TypeIdentity { get; }
        public int Value { get; }
    }
}
