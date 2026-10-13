using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;

namespace ThirdPersonSimulation
{
    public interface IRecordedRuntimeStateWriter
    {
        void Write<T>(T value) where T : unmanaged;
        void WriteSpan<T>(ReadOnlySpan<T> values) where T : unmanaged;
        void WriteText(CanonicalTextValue text);
    }

    public ref struct RecordedRuntimeStateReader
    {
        ReadOnlySpan<byte> m_Bytes;
        readonly CanonicalTextValue m_Text;
        CanonicalTextWriteBuffer m_TextCapture;
        int m_TextOffset;

        public RecordedRuntimeStateReader(ReadOnlySpan<byte> bytes, CanonicalTextValue text)
        {
            m_Bytes = bytes;
            m_Text = text;
            m_TextCapture = default;
            m_TextOffset = 0;
        }

        public RecordedRuntimeStateReader(ReadOnlySpan<byte> bytes, CanonicalTextWriteBuffer textCapture)
        {
            m_Bytes = bytes;
            m_Text = default;
            m_TextCapture = textCapture;
            m_TextOffset = 0;
        }

        public CanonicalTextValue ReadText()
        {
            int length = Read<int>();
            if (length < 0)
                return default;
            if (length == 0)
                return CanonicalTextValue.Empty;
            ReadOnlySpan<char> characters = ReadSpan<char>(length);
            if (m_TextCapture.IsValid)
                return m_TextCapture.Append(characters);
            CanonicalTextValue value = m_Text.Substring(m_TextOffset, length);
            m_TextOffset += length;
            return value;
        }

        public void Dispose() => m_TextCapture.Dispose();

        public T Read<T>() where T : unmanaged
        {
            T value = MemoryMarshal.Read<T>(m_Bytes);
            m_Bytes = m_Bytes.Slice(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)).Length);
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

    public struct CanonicalTextWriteBuffer : IDisposable
    {
        CanonicalTextStorage.Text m_Text;
        int m_Written;

        internal CanonicalTextWriteBuffer(CanonicalTextStorage.Text text)
        {
            m_Text = text;
            m_Written = 0;
        }

        public bool IsValid => m_Text != null;

        public CanonicalTextValue Append(ReadOnlySpan<char> characters)
        {
            m_Text.CaptureAt(m_Written, characters);
            var value = new CanonicalTextValue(m_Text, m_Written, characters.Length);
            m_Written += characters.Length;
            return value;
        }

        public CanonicalTextValue DetachWritten()
        {
            var value = new CanonicalTextValue(m_Text, 0, m_Written);
            this = default;
            return value;
        }

        public void Dispose()
        {
            m_Text?.Release();
            this = default;
        }
    }

    internal interface ICanonicalTextOwner
    {
        ReadOnlySpan<char> Span { get; }
        object ReadGate { get; }
        long LockOrder { get; }
        void Retain();
        void Release();
        void RetainLocked();
        void ReleaseLocked();
    }

    internal static class CanonicalTextOwnerOrdering
    {
        static long s_Next;
        internal static long Next() => Interlocked.Increment(ref s_Next);
    }

    public readonly struct CanonicalTextValue : IEquatable<CanonicalTextValue>, IComparable<CanonicalTextValue>
#if KK_DIAGNOSTIC_SAMPLING
        , KK.GeneratedDiagnosticSampling.IDiagnosticIdentityValue
#endif
    {
        readonly object m_Owner;
        readonly int m_Offset;
        readonly int m_Length;

        CanonicalTextValue(object owner, int offset, int length)
        {
            m_Owner = owner;
            m_Offset = offset;
            m_Length = length;
        }

        internal CanonicalTextValue(ICanonicalTextOwner text, int offset, int length)
            : this((object)text, offset, length)
        {
        }

        public static CanonicalTextValue FromString(string value) =>
            new CanonicalTextValue(value, 0, value == null ? 0 : value.Length);

        public static CanonicalTextValue Empty => FromString(string.Empty);
        public bool IsNull => m_Owner == null;
        public bool IsEmpty => m_Length == 0;
        public int Length => m_Length;
        internal ICanonicalTextOwner TextOwner => m_Owner as ICanonicalTextOwner;

        public CanonicalTextReadLease AcquireRead() => new CanonicalTextReadLease(TextOwner);

#if KK_DIAGNOSTIC_SAMPLING
        public KK.GeneratedDiagnosticSampling.DiagnosticIdentityValue CaptureIdentity() => m_Owner is string value
            ? KK.GeneratedDiagnosticSampling.DiagnosticIdentityValue.FromString(value, m_Offset, m_Length)
            : m_Owner == null ? KK.GeneratedDiagnosticSampling.DiagnosticIdentityValue.FromString(null)
            : KK.GeneratedDiagnosticSampling.DiagnosticIdentityValue.FromOwner(
                (KK.GeneratedDiagnosticSampling.IDiagnosticIdentityTextOwner)m_Owner, m_Offset, m_Length);
#endif

        public ReadOnlySpan<char> Span => m_Owner is string value
            ? value.AsSpan(m_Offset, m_Length)
            : m_Owner == null ? ReadOnlySpan<char>.Empty
            : ((ICanonicalTextOwner)m_Owner).Span.Slice(m_Offset, m_Length);

        public CanonicalTextValue Trim()
        {
            using CanonicalTextReadLease read = AcquireRead();
            ReadOnlySpan<char> characters = Span;
            int start = 0;
            int end = characters.Length;
            while (start < end && char.IsWhiteSpace(characters[start]))
                start++;
            while (end > start && char.IsWhiteSpace(characters[end - 1]))
                end--;
            return new CanonicalTextValue(m_Owner, m_Offset + start, end - start);
        }

        public ReadOnlySpan<char> AsSpan() => Span;
        public ReadOnlySpan<char> AsSpan(int start) => Span.Slice(start);
        public ReadOnlySpan<char> AsSpan(int start, int length) => Span.Slice(start, length);

        public CanonicalTextValue Substring(int start) => Substring(start, Length - start);

        public CanonicalTextValue Substring(int start, int length)
        {
            using CanonicalTextReadLease read = AcquireRead();
            ReadOnlySpan<char> slice = Span.Slice(start, length);
            return new CanonicalTextValue(m_Owner, m_Offset + start, slice.Length);
        }

        public bool StartsWith(CanonicalTextValue prefix, StringComparison comparison)
        {
            using TextComparisonReadLease read = new TextComparisonReadLease(in this, in prefix);
            return Span.StartsWith(prefix.Span, comparison);
        }

        public static bool IsNullOrEmpty(CanonicalTextValue value) => value.IsEmpty;

        public static bool IsNullOrWhiteSpace(CanonicalTextValue value)
        {
            using CanonicalTextReadLease read = value.AcquireRead();
            ReadOnlySpan<char> characters = value.Span;
            for (int index = 0; index < characters.Length; index++)
                if (!char.IsWhiteSpace(characters[index]))
                    return false;
            return true;
        }

        public static bool Equals(CanonicalTextValue left, CanonicalTextValue right, StringComparison comparison)
        {
            if (comparison == StringComparison.Ordinal)
                return left.Equals(right);
            if (left.IsNull || right.IsNull)
                return left.IsNull && right.IsNull;
            using TextComparisonReadLease read = new TextComparisonReadLease(in left, in right);
            return left.Span.Equals(right.Span, comparison);
        }

        public static int CompareOrdinal(CanonicalTextValue left, CanonicalTextValue right) => left.CompareTo(right);

        public void Retain()
        {
            if (m_Owner is ICanonicalTextOwner text)
                text.Retain();
        }

        public void Release()
        {
            if (m_Owner is ICanonicalTextOwner text)
                text.Release();
        }

        public bool Equals(CanonicalTextValue other)
        {
            if (IsNull || other.IsNull)
                return IsNull && other.IsNull;
            if (m_Length != other.m_Length)
                return false;
            if (ReferenceEquals(m_Owner, other.m_Owner) && m_Offset == other.m_Offset)
                return true;
            using TextComparisonReadLease read = new TextComparisonReadLease(in this, in other);
            return Span.SequenceEqual(other.Span);
        }

        public bool Equals(string other) => Equals(FromString(other));

        public int CompareTo(CanonicalTextValue other)
        {
            if (IsNull)
                return other.IsNull ? 0 : -1;
            if (other.IsNull)
                return 1;
            if (ReferenceEquals(m_Owner, other.m_Owner) && m_Offset == other.m_Offset && m_Length == other.m_Length)
                return 0;
            using TextComparisonReadLease read = new TextComparisonReadLease(in this, in other);
            return Span.SequenceCompareTo(other.Span);
        }
        public override bool Equals(object other) => other is CanonicalTextValue value && Equals(value);
        public override int GetHashCode()
        {
            if (IsNull)
                return 0;
            using CanonicalTextReadLease read = AcquireRead();
            return CanonicalTextEncoding.HashCode(Span);
        }
        public override string ToString()
        {
            if (IsNull)
                return null;
            if (m_Owner is string value && m_Offset == 0 && m_Length == value.Length)
                return value;
            using CanonicalTextReadLease read = AcquireRead();
            return new string(Span);
        }

        readonly struct TextComparisonReadLease : IDisposable
        {
            readonly CanonicalTextReadLease m_First;
            readonly CanonicalTextReadLease m_Second;

            internal TextComparisonReadLease(in CanonicalTextValue left, in CanonicalTextValue right)
            {
                ICanonicalTextOwner leftStorage = left.TextOwner;
                ICanonicalTextOwner rightStorage = right.TextOwner;
                bool reverse = leftStorage != null && rightStorage != null && leftStorage.LockOrder > rightStorage.LockOrder;
                m_First = reverse ? right.AcquireRead() : left.AcquireRead();
                m_Second = reverse ? left.AcquireRead() : right.AcquireRead();
            }

            public void Dispose()
            {
                m_Second.Dispose();
                m_First.Dispose();
            }
        }

        public static implicit operator CanonicalTextValue(string value) => FromString(value);
        public static bool operator ==(CanonicalTextValue left, CanonicalTextValue right) => left.Equals(right);
        public static bool operator !=(CanonicalTextValue left, CanonicalTextValue right) => !left.Equals(right);
    }

    public readonly struct CanonicalTextReadLease : IDisposable
    {
        readonly ICanonicalTextOwner m_Text;

        internal CanonicalTextReadLease(ICanonicalTextOwner text)
        {
            m_Text = text;
            if (text == null)
                return;
            Monitor.Enter(text.ReadGate);
            text.RetainLocked();
        }

        public void Dispose()
        {
            if (m_Text == null)
                return;
            m_Text.ReleaseLocked();
            Monitor.Exit(m_Text.ReadGate);
        }
    }

    public static class CanonicalTextEncoding
    {
        public const int FormatVersion = 2;
        const ulong OffsetBasis = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        public static int ByteCount(ReadOnlySpan<char> value) => checked(value.Length * sizeof(char));

        public static void Encode(ReadOnlySpan<char> value, Span<byte> destination)
        {
            for (int index = 0; index < value.Length; index++)
                BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(index * sizeof(char), sizeof(char)), value[index]);
        }

        public static void Decode(ReadOnlySpan<byte> value, Span<char> destination)
        {
            for (int index = 0; index < destination.Length; index++)
                destination[index] = (char)BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(index * sizeof(char), sizeof(char)));
        }

        public static string DecodeString(ArraySegment<byte> encoded)
        {
            if (encoded.Array == null)
                return null;
            if (encoded.Count == 0)
                return string.Empty;
            return string.Create(encoded.Count / sizeof(char), encoded,
                (characters, bytes) => Decode(bytes.AsSpan(), characters));
        }

        public static bool EqualsEncoded(ReadOnlySpan<byte> encoded, ReadOnlySpan<char> value)
        {
            if (encoded.Length != ByteCount(value))
                return false;
            for (int index = 0; index < value.Length; index++)
                if (BinaryPrimitives.ReadUInt16LittleEndian(encoded.Slice(index * sizeof(char), sizeof(char))) != value[index])
                    return false;
            return true;
        }

        public static int CompareEncoded(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        {
            int length = Math.Min(left.Length, right.Length) / sizeof(char);
            for (int index = 0; index < length; index++)
            {
                int order = BinaryPrimitives.ReadUInt16LittleEndian(left.Slice(index * sizeof(char), sizeof(char)))
                    .CompareTo(BinaryPrimitives.ReadUInt16LittleEndian(right.Slice(index * sizeof(char), sizeof(char))));
                if (order != 0)
                    return order;
            }
            return left.Length.CompareTo(right.Length);
        }

        public static ulong Hash(ReadOnlySpan<char> value)
        {
            ulong hash = OffsetBasis;
            for (int index = 0; index < value.Length; index++)
                hash = unchecked((hash ^ value[index]) * Prime);
            return hash;
        }

        public static ulong HashEncoded(ReadOnlySpan<byte> value)
        {
            ulong hash = OffsetBasis;
            for (int offset = 0; offset < value.Length; offset += sizeof(char))
                hash = unchecked((hash ^ BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(offset, sizeof(char)))) * Prime);
            return hash;
        }

        public static int HashCode(ReadOnlySpan<char> value)
        {
            ulong hash = Hash(value);
            return unchecked((int)(hash ^ (hash >> 32)));
        }
    }

    public interface ICanonicalTextCapture
    {
        CanonicalTextValue CaptureText(in CanonicalTextValue value);
    }

    public interface ICanonicalTextResolver
    {
        CanonicalTextValue ResolveText(ArraySegment<byte> utf16);
    }

    public sealed class CanonicalBytePayloadStorage
    {
        public sealed class Page : ICanonicalTextResolver
        {
            sealed class TextView : ICanonicalTextOwner
#if KK_DIAGNOSTIC_SAMPLING
                , KK.GeneratedDiagnosticSampling.IDiagnosticIdentityTextOwner
#endif
            {
                readonly Page m_Page;
                readonly int m_Alignment;

                internal TextView(Page page, int alignment)
                {
                    m_Page = page;
                    m_Alignment = alignment;
                }

                public ReadOnlySpan<char> Span => MemoryMarshal.Cast<byte, char>(
                    m_Page.Buffer.AsSpan(m_Alignment, (m_Page.Buffer.Length - m_Alignment) & ~1));
                public object ReadGate => m_Page.m_Owner.m_Free;
                public long LockOrder => m_Page.m_Owner.m_LockOrder;
                public void Retain() => m_Page.Retain();
                public void Release() => m_Page.Release();
                public void RetainLocked() => m_Page.RetainLocked();
                public void ReleaseLocked() => m_Page.ReleaseLocked();

#if KK_DIAGNOSTIC_SAMPLING
                object KK.GeneratedDiagnosticSampling.IDiagnosticIdentityTextOwner.ReadGate => ReadGate;
                ReadOnlySpan<char> KK.GeneratedDiagnosticSampling.IDiagnosticIdentityTextOwner.Text => Span;
                void KK.GeneratedDiagnosticSampling.IDiagnosticIdentityTextOwner.RetainText() => Retain();
                void KK.GeneratedDiagnosticSampling.IDiagnosticIdentityTextOwner.ReleaseText() => Release();
#endif
            }

            readonly CanonicalBytePayloadStorage m_Owner;
            readonly int m_Index;
            readonly TextView m_EvenText;
            readonly TextView m_OddText;
            int m_References;

            internal Page(CanonicalBytePayloadStorage owner, int index, int maximumPayloadBytes)
            {
                m_Owner = owner;
                m_Index = index;
                Buffer = new byte[maximumPayloadBytes];
                Writer = new CanonicalWriter(Buffer);
                m_EvenText = new TextView(this, 0);
                m_OddText = new TextView(this, 1);
            }

            public byte[] Buffer { get; }
            public CanonicalWriter Writer { get; }
            internal void Begin() => m_References = 1;

            public CanonicalTextValue ResolveText(ArraySegment<byte> utf16) =>
                new CanonicalTextValue((utf16.Offset & 1) == 0 ? m_EvenText : m_OddText,
                    utf16.Offset / sizeof(char), utf16.Count / sizeof(char));

            void Retain()
            {
                lock (m_Owner.m_Free)
                    RetainLocked();
            }

            internal void RetainLocked() => m_References++;

            public void Release()
            {
                lock (m_Owner.m_Free)
                    ReleaseLocked();
            }

            internal void ReleaseLocked()
            {
                if (--m_References != 0)
                    return;
                Writer.Reset();
                m_Owner.m_Free[m_Owner.m_FreeCount++] = m_Index;
            }
        }

        readonly Page[] m_Pages;
        readonly int[] m_Free;
        readonly long m_LockOrder;
        int m_FreeCount;

        public CanonicalBytePayloadStorage(int pageCount, int maximumPayloadBytes)
        {
            m_LockOrder = CanonicalTextOwnerOrdering.Next();
            m_Pages = new Page[pageCount];
            m_Free = new int[pageCount];
            for (int index = 0; index < pageCount; index++)
            {
                m_Pages[index] = new Page(this, index, maximumPayloadBytes);
                m_Free[index] = index;
            }
            m_FreeCount = pageCount;
        }

        public Page Acquire()
        {
            lock (m_Free)
            {
                if (m_FreeCount == 0)
                    throw new InvalidOperationException("Canonical payload storage exceeded its prepared retained batch capacity.");
                Page page = m_Pages[m_Free[--m_FreeCount]];
                page.Begin();
                return page;
            }
        }
    }

    public enum CanonicalTextCapacityKind : byte
    {
        Entries = 1,
        Characters = 2,
        TextValues = 3
    }

    public readonly struct CanonicalTextCapacityFailure
    {
        public CanonicalTextCapacityFailure(CanonicalTextCapacityKind kind, long required, int available)
        {
            Kind = kind;
            Required = required;
            Available = available;
        }

        public CanonicalTextCapacityKind Kind { get; }
        public long Required { get; }
        public int Available { get; }
    }

    public sealed class CanonicalTextCapacityException : InvalidOperationException
    {
        public CanonicalTextCapacityException(in CanonicalTextCapacityFailure failure)
            : base($"Canonical text exceeds the declared owner capacity: {failure.Kind}, required {failure.Required}, available {failure.Available}.")
        {
            Failure = failure;
        }

        public CanonicalTextCapacityFailure Failure { get; }
    }

    public sealed class CanonicalTextStorage
    {
        internal sealed class Text : ICanonicalTextOwner
#if KK_DIAGNOSTIC_SAMPLING
            , KK.GeneratedDiagnosticSampling.IDiagnosticIdentityTextOwner
#endif
        {
            readonly CanonicalTextStorage m_Owner;
            internal readonly int Index;
            internal int Start;
            internal int Length;
            int m_References;

            internal Text(CanonicalTextStorage owner, int index)
            {
                m_Owner = owner;
                Index = index;
            }

            internal ReadOnlySpan<char> Span => m_Owner.m_Characters.AsSpan(Start, Length);

            ReadOnlySpan<char> ICanonicalTextOwner.Span => Span;
            object ICanonicalTextOwner.ReadGate => m_Owner.ReadGate;
            long ICanonicalTextOwner.LockOrder => m_Owner.LockOrder;
            void ICanonicalTextOwner.Retain() => Retain();
            void ICanonicalTextOwner.Release() => Release();
            void ICanonicalTextOwner.RetainLocked() => RetainLocked();
            void ICanonicalTextOwner.ReleaseLocked() => ReleaseLocked();

#if KK_DIAGNOSTIC_SAMPLING
            object KK.GeneratedDiagnosticSampling.IDiagnosticIdentityTextOwner.ReadGate => m_Owner.ReadGate;
            ReadOnlySpan<char> KK.GeneratedDiagnosticSampling.IDiagnosticIdentityTextOwner.Text => Span;
            void KK.GeneratedDiagnosticSampling.IDiagnosticIdentityTextOwner.RetainText() => Retain();
            void KK.GeneratedDiagnosticSampling.IDiagnosticIdentityTextOwner.ReleaseText() => Release();
#endif

            internal void Begin(int start, int length)
            {
                Start = start;
                Length = length;
                m_References = 1;
            }

            internal CanonicalTextValue Receive(ArraySegment<byte> utf16, int offset)
            {
                lock (m_Owner.ReadGate)
                {
                    int length = utf16.Count / sizeof(char);
                    CanonicalTextEncoding.Decode(utf16.AsSpan(), m_Owner.m_Characters.AsSpan(Start + offset, length));
                    return new CanonicalTextValue(this, offset, length);
                }
            }

            internal CanonicalTextValue Capture(ReadOnlySpan<char> characters)
            {
                lock (m_Owner.ReadGate)
                {
                    characters.CopyTo(m_Owner.m_Characters.AsSpan(Start, Length));
                    return new CanonicalTextValue(this, 0, Length);
                }
            }

            internal void CaptureAt(int offset, ReadOnlySpan<char> characters)
            {
                lock (m_Owner.ReadGate)
                    characters.CopyTo(m_Owner.m_Characters.AsSpan(Start + offset, characters.Length));
            }

            internal void Retain()
            {
                lock (m_Owner.ReadGate)
                    RetainLocked();
            }

            internal void Release()
            {
                lock (m_Owner.ReadGate)
                    ReleaseLocked();
            }

            internal void RetainLocked() => m_References++;

            internal void ReleaseLocked()
            {
                if (--m_References == 0)
                    m_Owner.ReleaseLocked(this);
            }
        }

        struct CharacterRange
        {
            internal int Start;
            internal int Length;
        }

        readonly char[] m_Characters;
        readonly Text[] m_Values;
        readonly int[] m_Free;
        readonly CharacterRange[] m_FreeRanges;
        int m_FreeCount;
        int m_FreeRangeCount;
        int m_AvailableCharacters;

        public CanonicalTextStorage(int maximumTextValues, int totalCharacters)
        {
            LockOrder = CanonicalTextOwnerOrdering.Next();
            m_Characters = new char[totalCharacters];
            m_Values = new Text[maximumTextValues];
            m_Free = new int[maximumTextValues];
            m_FreeRanges = new CharacterRange[checked(maximumTextValues + 1)];
            for (int index = 0; index < maximumTextValues; index++)
            {
                m_Values[index] = new Text(this, index);
                m_Free[index] = index;
            }
            m_FreeCount = maximumTextValues;
            m_FreeRanges[0] = new CharacterRange { Length = totalCharacters };
            m_FreeRangeCount = totalCharacters == 0 ? 0 : 1;
            m_AvailableCharacters = totalCharacters;
        }

        public int MaximumTextValues => m_Values.Length;
        public int TotalCharacters => m_Characters.Length;
        public int AvailableTextValueCount => Volatile.Read(ref m_FreeCount);
        public int AvailableCharacters => Volatile.Read(ref m_AvailableCharacters);
        internal object ReadGate => m_Free;
        internal long LockOrder { get; }

        public CanonicalTextWriteBuffer BeginWrite(int maximumCharacters)
        {
            if (!TryAcquire(maximumCharacters, out Text text, out CanonicalTextCapacityFailure failure))
                throw new CanonicalTextCapacityException(in failure);
            return new CanonicalTextWriteBuffer(text);
        }

        internal bool TryAcquire(int characterCount, out Text text, out CanonicalTextCapacityFailure failure)
        {
            lock (m_Free)
            {
                if (m_FreeCount == 0)
                {
                    text = null;
                    failure = new CanonicalTextCapacityFailure(CanonicalTextCapacityKind.TextValues, 1, 0);
                    return false;
                }
                int rangeIndex = -1;
                for (int index = 0; index < m_FreeRangeCount; index++)
                {
                    int length = m_FreeRanges[index].Length;
                    if (length >= characterCount)
                    {
                        rangeIndex = index;
                        break;
                    }
                }
                if (rangeIndex < 0)
                {
                    if (characterCount > m_AvailableCharacters)
                    {
                        text = null;
                        failure = new CanonicalTextCapacityFailure(CanonicalTextCapacityKind.Characters,
                            characterCount, m_AvailableCharacters);
                        return false;
                    }
                    Compact();
                    rangeIndex = 0;
                }
                text = m_Values[m_Free[--m_FreeCount]];
                text.Begin(ReserveCharacters(rangeIndex, characterCount), characterCount);
                failure = default;
                return true;
            }
        }

        internal bool TryAppend(Text text, int characterCount, out int offset, out CanonicalTextCapacityFailure failure)
        {
            lock (m_Free)
            {
                if (characterCount > m_AvailableCharacters)
                {
                    offset = 0;
                    failure = new CanonicalTextCapacityFailure(CanonicalTextCapacityKind.Characters,
                        characterCount, m_AvailableCharacters);
                    return false;
                }
                int rangeIndex = -1;
                for (int index = 0; index < m_FreeRangeCount; index++)
                    if (m_FreeRanges[index].Start == text.Start + text.Length &&
                        m_FreeRanges[index].Length >= characterCount)
                    {
                        rangeIndex = index;
                        break;
                    }
                if (rangeIndex < 0)
                {
                    Compact();
                    int end = m_Characters.Length - m_AvailableCharacters;
                    int start = text.Start;
                    if (start + text.Length != end)
                    {
                        // 原地把接收块移到已占用区末尾，保留所有字段范围并继续写入。
                        Array.Reverse(m_Characters, start, text.Length);
                        Array.Reverse(m_Characters, start + text.Length, end - start - text.Length);
                        Array.Reverse(m_Characters, start, end - start);
                        for (int index = 0; index < m_Values.Length; index++)
                        {
                            Text value = m_Values[index];
                            if (value.Length != 0 && value.Start > start)
                                value.Start -= text.Length;
                        }
                        text.Start = end - text.Length;
                    }
                    rangeIndex = 0;
                }
                offset = text.Length;
                ReserveCharacters(rangeIndex, characterCount);
                text.Length += characterCount;
                failure = default;
                return true;
            }
        }

        int ReserveCharacters(int rangeIndex, int characterCount)
        {
            ref CharacterRange range = ref m_FreeRanges[rangeIndex];
            int start = range.Start;
            range.Start += characterCount;
            range.Length -= characterCount;
            if (range.Length == 0)
                RemoveRange(rangeIndex);
            m_AvailableCharacters -= characterCount;
            return start;
        }

        void Compact()
        {
            int sourceStart = 0;
            int releasedCharacters = 0;
            for (int index = 0; index < m_FreeRangeCount; index++)
            {
                ref CharacterRange range = ref m_FreeRanges[index];
                int length = range.Start - sourceStart;
                Array.Copy(m_Characters, sourceStart, m_Characters, sourceStart - releasedCharacters, length);
                sourceStart = range.Start + range.Length;
                releasedCharacters += range.Length;
                // 保留原空洞起点，累计长度供 descriptor 按原位置定位迁移距离。
                range.Length = releasedCharacters;
            }
            Array.Copy(m_Characters, sourceStart, m_Characters, sourceStart - releasedCharacters,
                m_Characters.Length - sourceStart);
            for (int index = 0; index < m_Values.Length; index++)
            {
                Text text = m_Values[index];
                if (text.Length == 0)
                    continue;
                int lower = 0;
                int upper = m_FreeRangeCount;
                while (lower < upper)
                {
                    int middle = lower + (upper - lower) / 2;
                    if (m_FreeRanges[middle].Start < text.Start)
                        lower = middle + 1;
                    else
                        upper = middle;
                }
                if (lower > 0)
                    text.Start -= m_FreeRanges[lower - 1].Length;
            }
            Array.Clear(m_FreeRanges, 0, m_FreeRangeCount);
            m_FreeRanges[0] = new CharacterRange
            {
                Start = m_Characters.Length - m_AvailableCharacters,
                Length = m_AvailableCharacters
            };
            m_FreeRangeCount = 1;
        }

        void RemoveRange(int index)
        {
            int count = --m_FreeRangeCount - index;
            Array.Copy(m_FreeRanges, index + 1, m_FreeRanges, index, count);
            m_FreeRanges[m_FreeRangeCount] = default;
        }

        void ReleaseLocked(Text text)
        {
            int next = 0;
            while (next < m_FreeRangeCount && m_FreeRanges[next].Start < text.Start)
                next++;
            bool joinsPrevious = next > 0 &&
                m_FreeRanges[next - 1].Start + m_FreeRanges[next - 1].Length == text.Start;
            bool joinsNext = next < m_FreeRangeCount &&
                text.Start + text.Length == m_FreeRanges[next].Start;
            if (joinsPrevious)
            {
                m_FreeRanges[next - 1].Length += text.Length;
                if (joinsNext)
                {
                    m_FreeRanges[next - 1].Length += m_FreeRanges[next].Length;
                    RemoveRange(next);
                }
            }
            else if (joinsNext)
            {
                m_FreeRanges[next].Start = text.Start;
                m_FreeRanges[next].Length += text.Length;
            }
            else
            {
                Array.Copy(m_FreeRanges, next, m_FreeRanges, next + 1, m_FreeRangeCount - next);
                m_FreeRanges[next] = new CharacterRange { Start = text.Start, Length = text.Length };
                m_FreeRangeCount++;
            }
            m_AvailableCharacters += text.Length;
            text.Start = text.Length = 0;
            m_Free[m_FreeCount++] = text.Index;
        }
    }

    public sealed class CanonicalTextReferences : ICanonicalTextCapture, ICanonicalTextResolver
    {
        readonly CanonicalTextValue[] m_Entries;
        readonly int[] m_Buckets;
        readonly CanonicalTextStorage m_Storage;
        CanonicalTextStorage.Text m_Reception;
        int m_Count;
        long m_CharacterCount;

        public CanonicalTextReferences(int entryCapacity, CanonicalTextStorage storage)
        {
            m_Entries = new CanonicalTextValue[entryCapacity];
            m_Buckets = new int[checked(entryCapacity * 2 + 1)];
            m_Storage = storage;
            ReceptionResolver = new TextReception(this);
        }

        public int Count => m_Count;
        public long CharacterCount => m_CharacterCount;
        public CanonicalTextStorage Storage => m_Storage;
        public ICanonicalTextResolver ReceptionResolver { get; }

        public void CopyTo(ICanonicalTextCapture capture)
        {
            for (int index = 0; index < m_Count; index++)
                capture.CaptureText(in m_Entries[index]);
        }

        public void Reset()
        {
            for (int index = 0; index < m_Count; index++)
            {
                ref readonly CanonicalTextValue value = ref m_Entries[index];
                ulong hash;
                using (CanonicalTextReadLease read = value.AcquireRead())
                    hash = CanonicalTextEncoding.Hash(value.Span);
                int bucket = (int)(hash % (ulong)m_Buckets.Length);
                while (m_Buckets[bucket] != index + 1)
                {
                    bucket++;
                    if (bucket == m_Buckets.Length)
                        bucket = 0;
                }
                m_Buckets[bucket] = 0;
                value.Release();
            }
            Array.Clear(m_Entries, 0, m_Count);
            m_Reception?.Release();
            m_Reception = null;
            m_Count = 0;
            m_CharacterCount = 0;
        }

        public CanonicalTextValue CaptureText(in CanonicalTextValue value)
        {
            if (!TryCaptureText(in value, out CanonicalTextValue captured, out CanonicalTextCapacityFailure failure))
                throw new CanonicalTextCapacityException(in failure);
            return captured;
        }

        public bool TryCaptureText(in CanonicalTextValue value, out CanonicalTextValue captured,
            out CanonicalTextCapacityFailure failure)
        {
            if (value.IsEmpty)
            {
                captured = value;
                failure = default;
                return true;
            }
            ulong hash;
            using (CanonicalTextReadLease read = value.AcquireRead())
                hash = CanonicalTextEncoding.Hash(value.Span);
            int bucket = FindBucket(in value, hash);
            int index = m_Buckets[bucket] - 1;
            if (index >= 0)
            {
                captured = m_Entries[index];
                failure = default;
                return true;
            }
            if (!TryAdmit(out failure))
            {
                captured = default;
                return false;
            }
            StoreText(in value, bucket);
            captured = value;
            return true;
        }

        public CanonicalTextValue CaptureCharacters(ReadOnlySpan<char> characters)
        {
            if (!TryCaptureCharacters(characters, out CanonicalTextValue value, out CanonicalTextCapacityFailure failure))
                throw new CanonicalTextCapacityException(in failure);
            return value;
        }

        public bool TryCaptureCharacters(ReadOnlySpan<char> characters, out CanonicalTextValue value,
            out CanonicalTextCapacityFailure failure)
        {
            if (characters.Length == 0)
            {
                value = CanonicalTextValue.Empty;
                failure = default;
                return true;
            }
            ulong hash = CanonicalTextEncoding.Hash(characters);
            int bucket = FindBucket(characters, hash);
            int index = m_Buckets[bucket] - 1;
            if (index >= 0)
            {
                value = m_Entries[index];
                failure = default;
                return true;
            }
            if (!TryAdmit(out failure))
            {
                value = default;
                return false;
            }
            if (!m_Storage.TryAcquire(characters.Length, out CanonicalTextStorage.Text text, out failure))
            {
                value = default;
                return false;
            }
            value = text.Capture(characters);
            StoreText(in value, bucket);
            value.Release();
            return true;
        }

        public CanonicalTextValue ReceiveText(ArraySegment<byte> utf16)
        {
            if (!TryReceiveText(utf16, out CanonicalTextValue value, out CanonicalTextCapacityFailure failure))
                throw new CanonicalTextCapacityException(in failure);
            return value;
        }

        public bool TryReceiveText(ArraySegment<byte> utf16, out CanonicalTextValue value,
            out CanonicalTextCapacityFailure failure)
        {
            if (utf16.Array == null)
            {
                value = default;
                failure = default;
                return true;
            }
            if (utf16.Count == 0)
            {
                value = CanonicalTextValue.Empty;
                failure = default;
                return true;
            }
            ulong hash = CanonicalTextEncoding.HashEncoded(utf16.AsSpan());
            int bucket = FindEncodedBucket(utf16.AsSpan(), hash);
            int index = m_Buckets[bucket] - 1;
            if (index >= 0)
            {
                value = m_Entries[index];
                failure = default;
                return true;
            }
            if (!TryAdmit(out failure))
            {
                value = default;
                return false;
            }
            int offset;
            if (m_Reception == null)
            {
                if (!m_Storage.TryAcquire(utf16.Count / sizeof(char), out m_Reception, out failure))
                {
                    value = default;
                    return false;
                }
                offset = 0;
            }
            else if (!m_Storage.TryAppend(m_Reception, utf16.Count / sizeof(char), out offset, out failure))
            {
                value = default;
                return false;
            }
            value = m_Reception.Receive(utf16, offset);
            StoreText(in value, bucket);
            return true;
        }

        public CanonicalTextValue ResolveText(ArraySegment<byte> utf16)
        {
            if (utf16.Array == null)
                return default;
            if (utf16.Count == 0)
                return CanonicalTextValue.Empty;
            int index = m_Buckets[FindEncodedBucket(utf16.AsSpan(), CanonicalTextEncoding.HashEncoded(utf16.AsSpan()))] - 1;
            if (index < 0)
                throw new InvalidOperationException("Canonical text is not held by the payload owner.");
            return m_Entries[index];
        }

        bool TryAdmit(out CanonicalTextCapacityFailure failure)
        {
            if (m_Count == m_Entries.Length)
            {
                failure = new CanonicalTextCapacityFailure(CanonicalTextCapacityKind.Entries, (long)m_Count + 1, m_Entries.Length);
                return false;
            }
            failure = default;
            return true;
        }

        void StoreText(in CanonicalTextValue value, int bucket)
        {
            value.Retain();
            m_Entries[m_Count] = value;
            m_Buckets[bucket] = ++m_Count;
            m_CharacterCount += value.Length;
        }

        int FindBucket(in CanonicalTextValue value, ulong hash)
        {
            int bucket = (int)(hash % (ulong)m_Buckets.Length);
            while (m_Buckets[bucket] != 0)
            {
                ref readonly CanonicalTextValue entry = ref m_Entries[m_Buckets[bucket] - 1];
                if (entry.Equals(value))
                    return bucket;
                bucket++;
                if (bucket == m_Buckets.Length)
                    bucket = 0;
            }
            return bucket;
        }

        int FindBucket(ReadOnlySpan<char> value, ulong hash)
        {
            int bucket = (int)(hash % (ulong)m_Buckets.Length);
            while (m_Buckets[bucket] != 0)
            {
                ref readonly CanonicalTextValue entry = ref m_Entries[m_Buckets[bucket] - 1];
                using (CanonicalTextReadLease read = entry.AcquireRead())
                    if (entry.Span.SequenceEqual(value))
                        return bucket;
                bucket++;
                if (bucket == m_Buckets.Length)
                    bucket = 0;
            }
            return bucket;
        }

        int FindEncodedBucket(ReadOnlySpan<byte> encoded, ulong hash)
        {
            int bucket = (int)(hash % (ulong)m_Buckets.Length);
            while (m_Buckets[bucket] != 0)
            {
                ref readonly CanonicalTextValue entry = ref m_Entries[m_Buckets[bucket] - 1];
                using (CanonicalTextReadLease read = entry.AcquireRead())
                    if (CanonicalTextEncoding.EqualsEncoded(encoded, entry.Span))
                        return bucket;
                bucket++;
                if (bucket == m_Buckets.Length)
                    bucket = 0;
            }
            return bucket;
        }

        sealed class TextReception : ICanonicalTextResolver
        {
            readonly CanonicalTextReferences m_Owner;

            internal TextReception(CanonicalTextReferences owner)
            {
                m_Owner = owner;
            }

            public CanonicalTextValue ResolveText(ArraySegment<byte> utf16) => m_Owner.ReceiveText(utf16);
        }

    }

    public sealed class CanonicalWriter : IDisposable, ICanonicalValueWriter
    {
        const int InitialCapacity = 256;

        byte[] m_Buffer;
        int m_Offset;
        int m_Capacity;
        int m_Position;
        int m_Length;
        bool m_Bounded;
        ICanonicalTextCapture m_TextCapture;

        public CanonicalWriter()
        {
            m_Buffer = new byte[InitialCapacity];
            m_Capacity = m_Buffer.Length;
        }

        public CanonicalWriter(byte[] buffer)
        {
            m_Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            m_Capacity = m_Buffer.Length;
            m_Bounded = true;
        }

        public long Length => m_Length;
        public ICanonicalTextCapture TextCapture => m_TextCapture;

        public void SetTextCapture(ICanonicalTextCapture capture) => m_TextCapture = capture;

        public long Position
        {
            get => m_Position;
            set
            {
                if (value < 0 || value > m_Length)
                    throw new ArgumentOutOfRangeException(nameof(value));
                m_Position = checked((int)value);
            }
        }

        public void Reset()
        {
            m_Position = 0;
            m_Length = 0;
        }

        public void Reset(ArraySegment<byte> destination)
        {
            m_Buffer = destination.Array;
            m_Offset = destination.Offset;
            m_Capacity = destination.Count;
            m_Bounded = true;
            Reset();
        }

        public void WriteByte(byte value)
        {
            EnsureCapacity(1);
            m_Buffer[m_Offset + m_Position++] = value;
            TrackLength();
        }

        public void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteSByte(sbyte value) => WriteByte(unchecked((byte)value));
        public void WriteInt16(short value) => WriteUInt16(unchecked((ushort)value));
        public void WriteChar(char value) => WriteUInt16(value);
        public void WriteSingleBits(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));
        public void WriteDoubleBits(double value) => WriteInt64(BitConverter.DoubleToInt64Bits(value));

        public void WriteInt32(int value)
        {
            EnsureCapacity(sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(m_Buffer.AsSpan(m_Offset + m_Position, sizeof(int)), value);
            m_Position += sizeof(int);
            TrackLength();
        }

        public void WriteUInt32(uint value)
        {
            EnsureCapacity(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(m_Buffer.AsSpan(m_Offset + m_Position, sizeof(uint)), value);
            m_Position += sizeof(uint);
            TrackLength();
        }

        public void WriteUInt16(ushort value)
        {
            EnsureCapacity(sizeof(ushort));
            BinaryPrimitives.WriteUInt16LittleEndian(m_Buffer.AsSpan(m_Offset + m_Position, sizeof(ushort)), value);
            m_Position += sizeof(ushort);
            TrackLength();
        }

        public void WriteInt64(long value)
        {
            EnsureCapacity(sizeof(long));
            BinaryPrimitives.WriteInt64LittleEndian(m_Buffer.AsSpan(m_Offset + m_Position, sizeof(long)), value);
            m_Position += sizeof(long);
            TrackLength();
        }

        public void WriteUInt64(ulong value)
        {
            EnsureCapacity(sizeof(ulong));
            BinaryPrimitives.WriteUInt64LittleEndian(m_Buffer.AsSpan(m_Offset + m_Position, sizeof(ulong)), value);
            m_Position += sizeof(ulong);
            TrackLength();
        }

        public void WriteDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            WriteInt64(BitConverter.DoubleToInt64Bits(value == 0d ? 0d : value));
        }

        public void WriteText(in CanonicalTextValue value)
        {
            if (value.IsNull)
            {
                WriteInt32(-1);
                return;
            }
            int byteCount = checked(value.Length * sizeof(char));
            WriteInt32(byteCount);
            if (byteCount == 0)
                return;
            EnsureCapacity(byteCount);
            using (CanonicalTextReadLease read = value.AcquireRead())
                CanonicalTextEncoding.Encode(value.Span, m_Buffer.AsSpan(m_Offset + m_Position, byteCount));
            m_Position += byteCount;
            TrackLength();
            m_TextCapture?.CaptureText(in value);
        }

        public void WriteRawText(ReadOnlySpan<char> value)
        {
            int byteCount = CanonicalTextEncoding.ByteCount(value);
            EnsureCapacity(byteCount);
            CanonicalTextEncoding.Encode(value, m_Buffer.AsSpan(m_Offset + m_Position, byteCount));
            m_Position += byteCount;
            TrackLength();
        }

        public void WriteBytes(byte[] value)
        {
            byte[] bytes = value ?? Array.Empty<byte>();
            WriteInt32(bytes.Length);
            WriteRaw(bytes);
        }

        public void WriteEventId(EventId value)
        {
            WriteInt32(value.IsValid ? 64 : 0);
            if (!value.IsValid)
                return;
            Span<char> characters = stackalloc char[64];
            Span<byte> bytes = stackalloc byte[64];
            value.Format(characters);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)characters[i];
            WriteRaw(bytes);
        }

        public void WriteHash<TValue>(TValue value) where TValue : struct, ICanonicalHashValue =>
            WriteHash(value.CanonicalHashValue);

        public void WriteHash(StableHash value)
        {
            WriteInt32(value.IsValid ? 64 : 0);
            WriteRawHash(value);
        }

        public void WriteRawHash(StableHash value)
        {
            if (!value.IsValid)
                return;
            Span<char> characters = stackalloc char[64];
            Span<byte> bytes = stackalloc byte[64];
            value.Format(characters);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)characters[i];
            WriteRaw(bytes);
        }

        public void WriteBytes(ReadOnlySpan<byte> value)
        {
            WriteInt32(value.Length);
            WriteRaw(value);
        }

        public long BeginLengthPrefixedBlock()
        {
            long position = m_Position;
            WriteInt32(0);
            return position;
        }

        public void EndLengthPrefixedBlock(long prefixPosition)
        {
            long end = m_Position;
            if (prefixPosition < 0 || prefixPosition > end - sizeof(int))
                throw new ArgumentOutOfRangeException(nameof(prefixPosition));
            int length = checked((int)(end - prefixPosition - sizeof(int)));
            Position = prefixPosition;
            WriteInt32(length);
            Position = end;
        }

        public void WriteRawBytes(byte[] value, int offset, int count)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (offset < 0 || count < 0 || offset > value.Length - count)
                throw new ArgumentOutOfRangeException();
            WriteRaw(value.AsSpan(offset, count));
        }

        public void WriteRawBytes(ReadOnlySpan<byte> value) => WriteRaw(value);

        public ReadOnlySpan<byte> WrittenSpan => m_Buffer.AsSpan(m_Offset, m_Length);
        public ArraySegment<byte> WrittenSegment => new ArraySegment<byte>(m_Buffer, m_Offset, m_Length);

        public byte[] ToArray()
        {
            var result = new byte[m_Length];
            Buffer.BlockCopy(m_Buffer, m_Offset, result, 0, m_Length);
            return result;
        }

        public bool ContentEquals(ReadOnlySpan<byte> value)
        {
            if (m_Length != value.Length)
                return false;
            return WrittenSpan.SequenceEqual(value);
        }

        public StableHash ComputeHash()
        {
            return SimulationCanonicalPayloadHash.Compute(WrittenSegment);
        }

        public void Dispose()
        {
        }

        void WriteRaw(ReadOnlySpan<byte> value)
        {
            if (value.Length == 0)
                return;
            EnsureCapacity(value.Length);
            value.CopyTo(m_Buffer.AsSpan(m_Offset + m_Position));
            m_Position += value.Length;
            TrackLength();
        }

        void EnsureCapacity(int count)
        {
            if (checked(m_Position + count) <= m_Capacity)
                return;
            if (m_Bounded)
                throw new InvalidOperationException("Bounded canonical writer capacity is exhausted.");
            int next = m_Buffer.Length;
            while (next < m_Position + count)
                next = checked(next * 2);
            Array.Resize(ref m_Buffer, next);
            m_Capacity = next;
        }

        void TrackLength()
        {
            if (m_Position > m_Length)
                m_Length = m_Position;
        }

    }

    public sealed class CanonicalReader : ICanonicalValueReader
    {
        byte[] m_Bytes;
        int m_End;
        int m_Offset;
        ICanonicalTextResolver m_TextResolver;

        public CanonicalReader(byte[] bytes)
            : this(new ArraySegment<byte>(bytes ?? throw new ArgumentNullException(nameof(bytes))))
        {
        }

        public CanonicalReader(ArraySegment<byte> bytes)
        {
            MatchWriter = new CanonicalMatchWriter(this);
            Reset(bytes);
        }

        public void Reset(ArraySegment<byte> bytes)
        {
            m_Bytes = bytes.Array ?? throw new ArgumentNullException(nameof(bytes));
            m_Offset = bytes.Offset;
            m_End = bytes.Offset + bytes.Count;
        }

        public CanonicalReader(ReadOnlyMemory<byte> bytes)
            : this(
                MemoryMarshal.TryGetArray(bytes, out ArraySegment<byte> segment)
                    ? segment
                    : throw new ArgumentException("Canonical reader payload must be array backed.", nameof(bytes)))
        {
        }

        public void Reset(ReadOnlyMemory<byte> bytes)
        {
            Reset(MemoryMarshal.TryGetArray(bytes, out ArraySegment<byte> segment)
                ? segment
                : throw new ArgumentException("Canonical reader payload must be array backed.", nameof(bytes)));
        }

        public int Remaining => m_End - m_Offset;
        public CanonicalMatchWriter MatchWriter { get; }
        public CanonicalPublishedValues PublishedValues { get; set; }
        public Func<CanonicalReader, string> DeclaredIdentityReader { private get; set; }
        public ICanonicalTextResolver TextResolver => m_TextResolver;
        public void SetTextResolver(ICanonicalTextResolver resolver) => m_TextResolver = resolver;
        public byte ReadByte()
        {
            Require(1);
            return m_Bytes[m_Offset++];
        }
        public bool ReadBoolean()
        {
            byte value = ReadByte();
            if (value > 1)
                throw new InvalidDataException("Canonical boolean is invalid.");
            return value == 1;
        }
        public sbyte ReadSByte() => unchecked((sbyte)ReadByte());
        public short ReadInt16() => unchecked((short)ReadUInt16());
        public char ReadChar() => (char)ReadUInt16();
        public float ReadSingleBits() => BitConverter.Int32BitsToSingle(ReadInt32());
        public double ReadDoubleBits() => BitConverter.Int64BitsToDouble(ReadInt64());
        public int ReadInt32() { Require(4); int value = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(m_Bytes, m_Offset, 4)); m_Offset += 4; return value; }
        public ushort ReadUInt16() { Require(2); ushort value = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(m_Bytes, m_Offset, 2)); m_Offset += 2; return value; }
        public uint ReadUInt32() { Require(4); uint value = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(m_Bytes, m_Offset, 4)); m_Offset += 4; return value; }
        public long ReadInt64() { Require(8); long value = BinaryPrimitives.ReadInt64LittleEndian(new ReadOnlySpan<byte>(m_Bytes, m_Offset, 8)); m_Offset += 8; return value; }
        public ulong ReadUInt64() { Require(8); ulong value = BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(m_Bytes, m_Offset, 8)); m_Offset += 8; return value; }
        public double ReadDouble()
        {
            double value = BitConverter.Int64BitsToDouble(ReadInt64());
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidDataException("Canonical double is not finite.");
            return value == 0d ? 0d : value;
        }
        // 返回原文本 owner 的借用视图；持久保存方在原持有边界 Retain/Release。
        public CanonicalTextValue ReadText() => ResolveText(ReadTextSegment());

        public string ReadString() => CanonicalTextEncoding.DecodeString(ReadTextSegment());

        public string ReadDeclaredString() => DeclaredIdentityReader(this);

        public void RequireText(in CanonicalTextValue expected)
        {
            ArraySegment<byte> encoded = ReadTextSegment();
            using CanonicalTextReadLease read = expected.AcquireRead();
            if (expected.IsNull != (encoded.Array == null) ||
                !expected.IsNull && !CanonicalTextEncoding.EqualsEncoded(encoded.AsSpan(), expected.Span))
                throw new InvalidDataException("Canonical text identity does not match its declared value.");
        }

        public CanonicalTextValue ResolveText(ArraySegment<byte> utf16)
        {
            if (utf16.Array == null)
                return default;
            if (utf16.Count == 0)
                return CanonicalTextValue.Empty;
            if (m_TextResolver == null)
                throw new InvalidOperationException("Canonical text reception requires its prepared text owner.");
            return m_TextResolver.ResolveText(utf16);
        }

        public void SkipText()
        {
            int length = ReadTextLength();
            if (length == -1)
                return;
            if ((length & 1) != 0)
                throw new InvalidDataException("Canonical UTF-16 text must contain complete code units.");
            Require(length);
            m_Offset += length;
        }

        public ArraySegment<byte> ReadTextSegment()
        {
            int length = ReadTextLength();
            if (length == -1)
                return default;
            if ((length & 1) != 0)
                throw new InvalidDataException("Canonical UTF-16 text must contain complete code units.");
            Require(length);
            var value = new ArraySegment<byte>(m_Bytes, m_Offset, length);
            m_Offset += length;
            return value;
        }

        public StableHash ReadHash() => StableHash.ParseUtf8(ReadRawBytesSegment(ReadLength()).AsSpan());

        public void RequireSchema(StableHash expected, string contract)
        {
            if (ReadInt32() != 64)
                throw new InvalidDataException($"Canonical schema format for '{contract}' is unsupported.");
            if (StableHash.ParseUtf8(ReadRawBytesSegment(64).AsSpan()) != expected)
                throw new InvalidDataException($"Canonical schema for '{contract}' does not match.");
        }

        public EventId ReadEventId()
        {
            int length = ReadLength();
            if (length == 0)
                return default;
            if (length != 64)
                throw new InvalidDataException("Canonical event identity must contain 64 hexadecimal characters.");
            Require(length);
            Span<char> characters = stackalloc char[64];
            for (int i = 0; i < characters.Length; i++)
                characters[i] = (char)m_Bytes[m_Offset + i];
            EventId value = EventId.Parse(characters);
            m_Offset += length;
            return value;
        }
        public byte[] ReadBytes()
        {
            int length = ReadLength();
            return ReadRawBytes(length);
        }
        public ArraySegment<byte> ReadBytesSegment()
        {
            return ReadRawBytesSegment(ReadLength());
        }

        public void SkipBytes() => ReadBytesSegment();
        public ArraySegment<byte> ReadRawBytesSegment(int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            Require(length);
            var value = new ArraySegment<byte>(m_Bytes, m_Offset, length);
            m_Offset += length;
            return value;
        }
        public byte[] ReadRawBytes(int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length));
            Require(length);
            if (length == 0)
                return Array.Empty<byte>();
            var value = new byte[length];
            Buffer.BlockCopy(m_Bytes, m_Offset, value, 0, length);
            m_Offset += length;
            return value;
        }
        public void RequireComplete()
        {
            if (Remaining != 0)
                throw new InvalidDataException($"Canonical payload has {Remaining} trailing bytes.");
        }

        int ReadTextLength()
        {
            int value = ReadInt32();
            if (value < -1)
                throw new InvalidDataException("Canonical text length is invalid.");
            return value;
        }

        int ReadLength()
        {
            int value = ReadInt32();
            if (value < 0)
                throw new InvalidDataException("Canonical length is negative.");
            return value;
        }

        void Require(int count)
        {
            if (count < 0 || count > Remaining)
                throw new EndOfStreamException();
        }
    }

}
