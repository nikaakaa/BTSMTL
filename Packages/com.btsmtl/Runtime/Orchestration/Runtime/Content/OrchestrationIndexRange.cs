using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationIndexRange
    {
        public OrchestrationIndexRange(ushort offset, ushort count)
        {
            Offset = offset;
            Count = count;
        }

        public readonly ushort Offset;
        public readonly ushort Count;
    }
}
