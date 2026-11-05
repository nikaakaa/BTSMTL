using System.Threading;

namespace BTSMTL.Preview
{
    public sealed class PreviewImagePacket
    {
        int m_InUse;

        public PreviewImagePacket(int capacity) => Buffer = new byte[capacity];

        public byte[] Buffer { get; }
        public int Length { get; private set; }

        public bool TryAcquire() => Interlocked.CompareExchange(ref m_InUse, 1, 0) == 0;
        public void SetLength(int length) => Length = length;
        public void Release() => Volatile.Write(ref m_InUse, 0);
    }
}
