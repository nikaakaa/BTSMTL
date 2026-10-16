using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ThirdPersonSimulation
{
    public interface IRecordedRuntimeStateWriter
    {
        int WrittenBytes { get; }
        void Write<T>(T value) where T : unmanaged;
        void WriteSpan<T>(ReadOnlySpan<T> values) where T : unmanaged;
        void WriteText(string text);
    }

    public ref struct RecordedRuntimeStateReader
    {
        ReadOnlySpan<byte> m_Bytes;
        readonly ReadOnlySpan<string> m_Texts;

        public RecordedRuntimeStateReader(ReadOnlySpan<byte> bytes, ReadOnlySpan<string> texts)
        {
            m_Bytes = bytes;
            m_Texts = texts;
        }

        public string ReadText() => m_Texts[Read<int>()];

        public void Skip(int byteCount) => m_Bytes = m_Bytes.Slice(byteCount);

        public T Read<T>() where T : unmanaged
        {
            T value = MemoryMarshal.Read<T>(m_Bytes);
            m_Bytes = m_Bytes.Slice(Unsafe.SizeOf<T>());
            return value;
        }

        public ReadOnlySpan<T> ReadSpan<T>(int count) where T : unmanaged
        {
            int bytes = checked(count * Unsafe.SizeOf<T>());
            ReadOnlySpan<T> result = MemoryMarshal.Cast<byte, T>(m_Bytes.Slice(0, bytes));
            m_Bytes = m_Bytes.Slice(bytes);
            return result;
        }
    }
}
