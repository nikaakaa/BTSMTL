using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Preview
{
    public sealed class PreviewStateLayout
    {
        readonly IPreviewStateAdapter[] m_Adapters;
        readonly int[] m_ByteOffsets;
        readonly int[] m_TextOffsets;
        readonly int[] m_RestoreOrder;

        public PreviewStateLayout(string identity, IReadOnlyList<IPreviewStateAdapter> adapters)
        {
            Identity = identity;
            m_Adapters = new IPreviewStateAdapter[adapters.Count];
            m_ByteOffsets = new int[adapters.Count + 1];
            m_TextOffsets = new int[adapters.Count + 1];
            m_RestoreOrder = new int[adapters.Count];
            var identities = new HashSet<string>(StringComparer.Ordinal);
            RestoreSupport = PreviewRestoreSupport.Verified;
            for (int i = 0; i < adapters.Count; i++)
            {
                IPreviewStateAdapter adapter = adapters[i];
                if (!identities.Add(adapter.Identity))
                    throw new ArgumentException("预览存档组件身份重复：" + adapter.Identity, nameof(adapters));
                m_Adapters[i] = adapter;
                m_ByteOffsets[i + 1] = checked(m_ByteOffsets[i] + adapter.Capacity.Bytes);
                m_TextOffsets[i + 1] = checked(m_TextOffsets[i] + adapter.Capacity.TextReferences);
                m_RestoreOrder[i] = i;
                if ((byte)adapter.RestoreSupport < (byte)RestoreSupport)
                {
                    RestoreSupport = adapter.RestoreSupport;
                    Limitation = adapter.Identity + ": " + adapter.Limitation;
                }
            }
            Array.Sort(m_RestoreOrder, (left, right) =>
            {
                int stage = m_Adapters[left].RestoreStage.CompareTo(m_Adapters[right].RestoreStage);
                return stage != 0 ? stage : left.CompareTo(right);
            });
        }

        public string Identity { get; }
        public int ByteCapacity => m_ByteOffsets[m_Adapters.Length];
        public int TextReferenceCapacity => m_TextOffsets[m_Adapters.Length];
        public PreviewRestoreSupport RestoreSupport { get; }
        public string Limitation { get; }

        public PreviewStateSnapshot CreateSnapshot()
        {
            if (RestoreSupport == PreviewRestoreSupport.RestartOnly)
                throw new InvalidOperationException("预览适配仅支持从头重跑：" + Limitation);
            return new PreviewStateSnapshot(this);
        }

        internal void Write(byte[] bytes, string[] texts)
        {
            Array.Clear(texts, 0, texts.Length);
            for (int i = 0; i < m_Adapters.Length; i++)
            {
                var writer = new PreviewStateBufferWriter(bytes, m_ByteOffsets[i], texts, m_TextOffsets[i]);
                m_Adapters[i].Write(ref writer);
            }
        }

        internal void Restore(byte[] bytes, string[] texts)
        {
            for (int order = 0; order < m_RestoreOrder.Length; order++)
            {
                int i = m_RestoreOrder[order];
                var reader = new RecordedRuntimeStateReader(
                    bytes.AsSpan(m_ByteOffsets[i], m_ByteOffsets[i + 1] - m_ByteOffsets[i]),
                    texts.AsSpan(m_TextOffsets[i], m_TextOffsets[i + 1] - m_TextOffsets[i]));
                m_Adapters[i].Restore(ref reader);
            }
            Physics.SyncTransforms();
        }
    }

    public sealed class PreviewStateSnapshot
    {
        readonly PreviewStateLayout m_Layout;
        readonly byte[] m_Bytes;
        readonly string[] m_Texts;

        internal PreviewStateSnapshot(PreviewStateLayout layout)
        {
            m_Layout = layout;
            m_Bytes = new byte[layout.ByteCapacity];
            m_Texts = new string[layout.TextReferenceCapacity];
        }

        public string LayoutIdentity => m_Layout.Identity;
        public long Frame { get; private set; } = -1;
        public ReadOnlyMemory<byte> Bytes => m_Bytes;
        public ReadOnlyMemory<string> Texts => m_Texts;

        public void Capture(long frame)
        {
            Frame = -1;
            m_Layout.Write(m_Bytes, m_Texts);
            Frame = frame;
        }

        public void Restore() => m_Layout.Restore(m_Bytes, m_Texts);
    }

    struct PreviewStateBufferWriter : IRecordedRuntimeStateWriter
    {
        readonly byte[] m_Bytes;
        readonly string[] m_Texts;
        readonly int m_ByteStart;
        readonly int m_TextStart;
        int m_TextCount;

        public PreviewStateBufferWriter(byte[] bytes, int byteStart, string[] texts, int textStart)
        {
            m_Bytes = bytes;
            m_Texts = texts;
            m_ByteStart = byteStart;
            m_TextStart = textStart;
            m_TextCount = 0;
            WrittenBytes = 0;
        }

        public int WrittenBytes { get; private set; }

        public void Write<T>(T value) where T : unmanaged
        {
            MemoryMarshal.Write(m_Bytes.AsSpan(m_ByteStart + WrittenBytes), ref value);
            WrittenBytes += Unsafe.SizeOf<T>();
        }

        public void WriteSpan<T>(ReadOnlySpan<T> values) where T : unmanaged
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(values);
            bytes.CopyTo(m_Bytes.AsSpan(m_ByteStart + WrittenBytes));
            WrittenBytes += bytes.Length;
        }

        public void WriteText(string text)
        {
            m_Texts[m_TextStart + m_TextCount] = text;
            Write(m_TextCount++);
        }
    }
}
