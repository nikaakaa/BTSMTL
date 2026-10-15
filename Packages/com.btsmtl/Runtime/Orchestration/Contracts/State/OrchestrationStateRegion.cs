using System.Runtime.InteropServices;

namespace BTSMTL.Orchestration
{
    public readonly unsafe struct OrchestrationStateRegion<T> where T : unmanaged
    {
        [StructLayout(LayoutKind.Sequential)]
        struct AlignmentProbe
        {
            public byte Prefix;
            public T Value;
        }

        public OrchestrationStateRegion(int offsetBytes, int length)
        {
            OffsetBytes = offsetBytes;
            Length = length;
        }

        public int OffsetBytes { get; }
        public int Length { get; }
        public int SizeBytes => checked(sizeof(T) * Length);
        public static int ElementSizeBytes => sizeof(T);
        public static int ElementAlignment => sizeof(AlignmentProbe) - sizeof(T);
    }
}
