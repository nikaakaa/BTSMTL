using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationGraphContent<TValue> where TValue : unmanaged
    {
        public OrchestrationGraphContent(GraphExecutionIdentity identity,
            OrchestrationGraphStructure structure, OrchestrationGraphParameters<TValue> parameters,
            OrchestrationGraphSourceMap sources)
        {
            Identity = identity;
            Structure = structure;
            Parameters = parameters;
            Sources = sources;
            OrchestrationGraphParameterLayout layout = structure.ParameterLayout;
            if (layout.ConstantCount != parameters.Constants.Length || layout.DefaultValueCount != parameters.DefaultValues.Length ||
                layout.OperationPayloadByteCount != parameters.OperationPayloads.Length ||
                layout.OperationContents.Length != parameters.OperationContents.Length || structure.Sources.Length != sources.Entries.Length)
                throw new System.IO.InvalidDataException("图结构的参数槽或来源布局与配对分块不一致。");
            Identities = OrchestrationIdentityPublication.Extend(structure.Identities, parameters.Identities.Entries.ToArray());
        }

        public GraphExecutionIdentity Identity { get; }
        public OrchestrationGraphStructure Structure { get; }
        public OrchestrationGraphParameters<TValue> Parameters { get; }
        public OrchestrationGraphSourceMap Sources { get; }
        [ValueStorageIgnore] public OrchestrationIdentityTable Identities { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationGraphStructure
    {
        public OrchestrationGraphStructure(SimulationNumericProfile numericProfile,
            OrchestrationIdentityTable identities, OrchestrationProgramContent program,
            OrchestrationGraphParameterLayout parameterLayout,
            ReadOnlyMemory<OrchestrationExtensionReference> extensions,
            ReadOnlyMemory<OrchestrationGraphEntry> entries,
            ReadOnlyMemory<OrchestrationGraphVariable> variables,
            ReadOnlyMemory<OrchestrationGraphBlackboardBinding> blackboardBindings,
            ReadOnlyMemory<OrchestrationGraphHostInput> hostInputs,
            ReadOnlyMemory<OrchestrationGraphPort> ports,
            ReadOnlyMemory<OrchestrationGraphExecutionSource> sources)
        {
            NumericProfile = numericProfile;
            Identities = identities;
            Program = program;
            ParameterLayout = parameterLayout;
            Extensions = extensions;
            Entries = entries;
            Variables = variables;
            BlackboardBindings = blackboardBindings;
            HostInputs = hostInputs;
            Ports = ports;
            Sources = sources;
        }

        public SimulationNumericProfile NumericProfile { get; }
        public OrchestrationIdentityTable Identities { get; }
        public OrchestrationProgramContent Program { get; }
        public OrchestrationGraphParameterLayout ParameterLayout { get; }
        public ReadOnlyMemory<OrchestrationExtensionReference> Extensions { get; }
        public ReadOnlyMemory<OrchestrationGraphEntry> Entries { get; }
        public ReadOnlyMemory<OrchestrationGraphVariable> Variables { get; }
        public ReadOnlyMemory<OrchestrationGraphBlackboardBinding> BlackboardBindings { get; }
        public ReadOnlyMemory<OrchestrationGraphHostInput> HostInputs { get; }
        public ReadOnlyMemory<OrchestrationGraphPort> Ports { get; }
        public ReadOnlyMemory<OrchestrationGraphExecutionSource> Sources { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationGraphParameters<TValue> where TValue : unmanaged
    {
        public OrchestrationGraphParameters(StableHash sourceIdentity, OrchestrationIdentityTable identities,
            ReadOnlyMemory<byte> operationPayloads, ReadOnlyMemory<TValue> constants,
            ReadOnlyMemory<TValue> defaultValues, ReadOnlyMemory<ReadOnlyMemory<byte>> operationContents)
        {
            SourceIdentity = sourceIdentity;
            Identities = identities;
            OperationPayloads = operationPayloads;
            Constants = constants;
            DefaultValues = defaultValues;
            OperationContents = operationContents;
        }

        public StableHash SourceIdentity { get; }
        public OrchestrationIdentityTable Identities { get; }
        public ReadOnlyMemory<byte> OperationPayloads { get; }
        public ReadOnlyMemory<TValue> Constants { get; }
        public ReadOnlyMemory<TValue> DefaultValues { get; }
        public ReadOnlyMemory<ReadOnlyMemory<byte>> OperationContents { get; }

        public T ReadOperationPayload<T>(OrchestrationProgramContent structure, int operationIndex) where T : unmanaged =>
            SpanReinterpret.Read<T>(OperationPayloads.Span.Slice(structure.Operations.Span[operationIndex].ParameterOffset));
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationGraphParameterLayout
    {
        public OrchestrationGraphParameterLayout(int constantCount, int defaultValueCount, int operationPayloadByteCount,
            ReadOnlyMemory<OrchestrationOperationContentBinding> operationContents)
        {
            ConstantCount = constantCount;
            DefaultValueCount = defaultValueCount;
            OperationPayloadByteCount = operationPayloadByteCount;
            OperationContents = operationContents;
        }

        public int ConstantCount { get; }
        public int DefaultValueCount { get; }
        public int OperationPayloadByteCount { get; }
        public ReadOnlyMemory<OrchestrationOperationContentBinding> OperationContents { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationGraphSourceMap
    {
        public OrchestrationGraphSourceMap(ReadOnlyMemory<OrchestrationGraphSourceText> entries) => Entries = entries;

        public ReadOnlyMemory<OrchestrationGraphSourceText> Entries { get; }

        public ReadOnlyMemory<OrchestrationGraphSource> Bind(ReadOnlySpan<OrchestrationGraphExecutionSource> sources)
        {
            var result = new OrchestrationGraphSource[sources.Length];
            for (int i = 0; i < result.Length; i++)
            {
                OrchestrationGraphSourceText text = Entries.Span[i];
                result[i] = new OrchestrationGraphSource(sources[i], text.SourceType, text.DisplayPathSegments);
            }
            return result;
        }
    }
}
