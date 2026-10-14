namespace ThirdPersonSimulation
{
    public enum PortableModifierOperation : byte
    {
        Additive = 0,
        Multiplicative = 1,
        Override = 2,
        Clamp = 3
    }

    public enum PortableClampBound : byte
    {
        Minimum = 0,
        Maximum = 1
    }
}
