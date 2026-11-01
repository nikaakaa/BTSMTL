using System;
using System.IO;
using System.Runtime.CompilerServices;
using BTSMTL.Orchestration;
using Float32Value = BTSMTL.Orchestration.Float32OrchestrationValue;
using FPValue = ThirdPersonSimulation.QuantumNumerics.FPOrchestrationValue;

namespace ThirdPersonSimulation.Serialization
{
    public sealed partial class MemoryPackGraphContentPublisher :
        IOrchestrationGraphContentEncoding<Float32Value>, IOrchestrationGraphContentEncoding<FPValue>
    {
        public const string Float32OrchestrationContractIdentity = "btsmtl.float32-orchestration.memorypack/9";
        public const string FPOrchestrationContractIdentity = "btsmtl.fp-orchestration.memorypack/9";
        const string OrchestrationEnvelopeContract = "btsmtl.graph-blocks/9";

        sealed class EncodedGraphBlock
        {
            internal EncodedGraphBlock(byte[] bytes)
            {
                Bytes = bytes;
                Identity = StableHash.Compute(bytes);
            }

            internal readonly byte[] Bytes;
            internal readonly StableHash Identity;
        }

        static readonly ConditionalWeakTable<object, EncodedGraphBlock> s_GraphBlocks = new();

        static EncodedGraphBlock EncodeGraphBlock<T>(T value) where T : class =>
            s_GraphBlocks.GetValue(value, _ => new EncodedGraphBlock(MemoryPackContent.Serialize(value)));

        string IOrchestrationGraphContentEncoding<Float32Value>.EncodingContract => Float32OrchestrationContractIdentity;
        string IOrchestrationGraphContentEncoding<FPValue>.EncodingContract => FPOrchestrationContractIdentity;

        public OrchestrationGraphContent<Float32Value> Create(GraphCompilationIdentity source,
            OrchestrationGraphStructure structure, OrchestrationGraphParameters<Float32Value> parameters,
            OrchestrationGraphSourceMap sources, OrchestrationGraphContent<Float32Value> previous) =>
            CreateGraphContent(source, structure, parameters, sources, previous,
                MemoryPackContent.GetSchemaHash<OrchestrationGraphParameters<Float32Value>>());

        public OrchestrationGraphContent<FPValue> Create(GraphCompilationIdentity source,
            OrchestrationGraphStructure structure, OrchestrationGraphParameters<FPValue> parameters,
            OrchestrationGraphSourceMap sources, OrchestrationGraphContent<FPValue> previous) =>
            CreateGraphContent(source, structure, parameters, sources, previous,
                MemoryPackContent.GetSchemaHash<OrchestrationGraphParameters<FPValue>>());

        static OrchestrationGraphContent<TValue> CreateGraphContent<TValue>(GraphCompilationIdentity source,
            OrchestrationGraphStructure structure, OrchestrationGraphParameters<TValue> parameters,
            OrchestrationGraphSourceMap sources, OrchestrationGraphContent<TValue> previous,
            StableHash parameterSchema) where TValue : unmanaged
        {
            StableHash state = previous != null && ReferenceEquals(previous.Structure, structure)
                ? previous.Identity.StateLayoutIdentity
                : StableHash.Compute(MemoryPackContent.Serialize((structure.Program.ExecutionStorage,
                    structure.Program.ValueSlotTypes, structure.Program.ValueKindSlotCounts, structure.Program.WorkspaceCapacity)));
            return new OrchestrationGraphContent<TValue>(new GraphExecutionIdentity(source,
                StableHash.Compute(MemoryPackContent.GetSchemaHash<OrchestrationGraphStructure>().Value,
                    EncodeGraphBlock(structure).Identity.Value), parameters.SourceIdentity,
                StableHash.Compute(parameterSchema.Value, EncodeGraphBlock(parameters).Identity.Value), state,
                StableHash.Compute(MemoryPackContent.GetSchemaHash<OrchestrationGraphSourceMap>().Value,
                    EncodeGraphBlock(sources).Identity.Value)), structure, parameters, sources);
        }

        public byte[] Write(OrchestrationGraphContent<Float32Value> content) => WriteGraphContent(content);

        public byte[] Write(OrchestrationGraphContent<FPValue> content) => WriteGraphContent(content);

        public OrchestrationGraphContentBlocks WriteBlocks(OrchestrationGraphContent<Float32Value> content) =>
            PublishGraphBlocks(content);

        public OrchestrationGraphContentBlocks WriteBlocks(OrchestrationGraphContent<FPValue> content) =>
            PublishGraphBlocks(content);

        static OrchestrationGraphContentBlocks PublishGraphBlocks<TValue>(OrchestrationGraphContent<TValue> content)
            where TValue : unmanaged => new(content.Identity, EncodeGraphBlock(content.Structure).Bytes,
                EncodeGraphBlock(content.Parameters).Bytes, EncodeGraphBlock(content.Sources).Bytes);

        static byte[] WriteGraphContent<TValue>(OrchestrationGraphContent<TValue> content) where TValue : unmanaged
        {
            OrchestrationGraphContentBlocks blocks = PublishGraphBlocks(content);
            return MemoryPackContent.Serialize((OrchestrationEnvelopeContract, blocks.Identity,
                blocks.Structure, blocks.Parameters, blocks.Sources));
        }

        OrchestrationGraphContent<Float32Value> IOrchestrationGraphContentEncoding<Float32Value>.ReadBlocks(
            OrchestrationGraphContentBlocks blocks, GraphExecutionIdentity expected) =>
            ReadGraphBlocks<Float32Value>(blocks, expected, Float32OrchestrationContractIdentity, "Float32");

        OrchestrationGraphContent<FPValue> IOrchestrationGraphContentEncoding<FPValue>.ReadBlocks(
            OrchestrationGraphContentBlocks blocks, GraphExecutionIdentity expected) =>
            ReadGraphBlocks<FPValue>(blocks, expected, FPOrchestrationContractIdentity, "FP");

        OrchestrationGraphContent<Float32Value> IOrchestrationGraphContentEncoding<Float32Value>.Read(
            byte[] bytes, GraphExecutionIdentity expected)
        {
            return ReadGraphContent<Float32Value>(bytes, expected, Float32OrchestrationContractIdentity, "Float32");
        }

        OrchestrationGraphContent<FPValue> IOrchestrationGraphContentEncoding<FPValue>.Read(
            byte[] bytes, GraphExecutionIdentity expected)
        {
            return ReadGraphContent<FPValue>(bytes, expected, FPOrchestrationContractIdentity, "FP");
        }

        static OrchestrationGraphContent<TValue> ReadGraphContent<TValue>(byte[] bytes,
            GraphExecutionIdentity expected, string encodingContract, string numericTarget) where TValue : unmanaged
        {
            var envelope = MemoryPackContent.Deserialize<(string Contract, GraphExecutionIdentity Identity,
                byte[] Structure, byte[] Parameters, byte[] Sources)>(bytes);
            if (envelope.Contract != OrchestrationEnvelopeContract)
                throw new InvalidDataException("图产物不是当前分块格式，请沿正式入口重新发布。");
            return ReadGraphBlocks<TValue>(new OrchestrationGraphContentBlocks(envelope.Identity,
                envelope.Structure, envelope.Parameters, envelope.Sources), expected, encodingContract, numericTarget);
        }

        static OrchestrationGraphContent<TValue> ReadGraphBlocks<TValue>(OrchestrationGraphContentBlocks blocks,
            GraphExecutionIdentity expected, string encodingContract, string numericTarget) where TValue : unmanaged
        {
            RequireOrchestrationIdentity(blocks.Identity, expected, encodingContract, numericTarget);
            OrchestrationGraphStructure structure = MemoryPackContent.Deserialize<OrchestrationGraphStructure>(blocks.Structure);
            OrchestrationGraphParameters<TValue> parameters = MemoryPackContent.Deserialize<OrchestrationGraphParameters<TValue>>(blocks.Parameters);
            OrchestrationGraphSourceMap sources = MemoryPackContent.Deserialize<OrchestrationGraphSourceMap>(blocks.Sources);
            s_GraphBlocks.Add(structure, new EncodedGraphBlock(blocks.Structure));
            s_GraphBlocks.Add(parameters, new EncodedGraphBlock(blocks.Parameters));
            s_GraphBlocks.Add(sources, new EncodedGraphBlock(blocks.Sources));
            OrchestrationGraphContent<TValue> content = CreateGraphContent(blocks.Identity.Source, structure, parameters, sources, null,
                MemoryPackContent.GetSchemaHash<OrchestrationGraphParameters<TValue>>());
            content.Identity.RequireMatches(blocks.Identity);
            return content;
        }

        static void RequireOrchestrationIdentity(GraphExecutionIdentity actual, GraphExecutionIdentity expected,
            string encodingContract, string numericTarget)
        {
            if (actual.Source.EncodingContract != encodingContract || actual.Source.NodeContract != GraphNodeSemanticContract.Identity ||
                actual.Source.NumericTarget != numericTarget)
                throw new InvalidDataException("图产物的编码、节点语义或数值目标与当前编排核心不匹配。");
            actual.RequireMatches(expected);
        }
    }
}
