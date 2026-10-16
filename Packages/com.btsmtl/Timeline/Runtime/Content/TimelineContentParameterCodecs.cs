using System;
using System.Collections.Generic;

namespace BTSMTL.Timeline
{
    public delegate TParameters TimelineContentParameterReader<TParameters>(ReadOnlySpan<byte> bytes)
        where TParameters : TimelineContentParameters;

    public interface ITimelineContentParameterCodec
    {
        string ContractKind { get; }
        Type ParameterType { get; }
        byte[] Write(TimelineContentParameters parameters);
        TimelineContentParameters Read(ReadOnlySpan<byte> bytes);
    }

    public sealed class TimelineContentParameterCodec<TParameters> : ITimelineContentParameterCodec
        where TParameters : TimelineContentParameters
    {
        readonly Func<TParameters, byte[]> m_Write;
        readonly TimelineContentParameterReader<TParameters> m_Read;

        public TimelineContentParameterCodec(string contractKind, Func<TParameters, byte[]> write,
            TimelineContentParameterReader<TParameters> read)
        {
            ContractKind = contractKind;
            m_Write = write;
            m_Read = read;
        }

        public string ContractKind { get; }
        public Type ParameterType => typeof(TParameters);
        public byte[] Write(TimelineContentParameters parameters) => m_Write((TParameters)parameters);
        public TimelineContentParameters Read(ReadOnlySpan<byte> bytes) => m_Read(bytes);
    }

    public sealed class TimelineContentParameterCodecs : IServiceProvider
    {
        readonly Dictionary<string, ITimelineContentParameterCodec> m_Codecs;

        public TimelineContentParameterCodecs(IEnumerable<ITimelineContentParameterCodec> codecs)
        {
            m_Codecs = new Dictionary<string, ITimelineContentParameterCodec>(StringComparer.Ordinal);
            foreach (ITimelineContentParameterCodec codec in codecs)
                if (!m_Codecs.TryAdd(codec.ContractKind, codec))
                    throw new InvalidOperationException($"Duplicate Timeline parameter codec '{codec.ContractKind}'.");
        }

        public ITimelineContentParameterCodec Require(string contractKind) =>
            m_Codecs.TryGetValue(contractKind, out ITimelineContentParameterCodec codec)
                ? codec
                : throw new InvalidOperationException($"Timeline parameter codec '{contractKind}' is not composed by this owner.");

        public object GetService(Type serviceType) =>
            serviceType == typeof(TimelineContentParameterCodecs) ? this : null;
    }
}
