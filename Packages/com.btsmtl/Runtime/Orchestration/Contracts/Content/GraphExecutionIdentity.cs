using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ThirdPersonSimulation
{
    public static class GraphNodeSemanticContract
    {
        public const string Identity = "btsmtl-operations/8";
    }

    public enum GraphExecutionDependencyKind : byte
    {
        GraphSource = 1,
        RuntimeContent = 2
    }

    [ValueStorage(UseConstructor = true)]
    public readonly partial struct GraphExecutionDependency
    {
        public GraphExecutionDependency(string identity, string revision, GraphExecutionDependencyKind kind)
        {
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            Revision = SimulationIdentity.Require(revision, nameof(revision));
            Kind = kind;
        }

        public string Identity { get; }
        public string Revision { get; }
        public GraphExecutionDependencyKind Kind { get; }

        [ValueValidation]
        void RequireIdentity()
        {
            SimulationIdentity.Require(Identity, nameof(Identity));
            SimulationIdentity.Require(Revision, nameof(Revision));
        }
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class GraphCompilationIdentity
    {
        public GraphCompilationIdentity(string graphId, string sourceRevision, string encodingContract,
            string nodeContract, string numericTarget, string compilationContract,
            IReadOnlyList<GraphExecutionDependency> dependencies)
        {
            GraphId = SimulationIdentity.Require(graphId, nameof(graphId));
            SourceRevision = SimulationIdentity.Require(sourceRevision, nameof(sourceRevision));
            EncodingContract = SimulationIdentity.Require(encodingContract, nameof(encodingContract));
            NodeContract = SimulationIdentity.Require(nodeContract, nameof(nodeContract));
            NumericTarget = SimulationIdentity.Require(numericTarget, nameof(numericTarget));
            CompilationContract = SimulationIdentity.Require(compilationContract, nameof(compilationContract));
            Dependencies = Array.AsReadOnly(dependencies.OrderBy(value => value.Identity, StringComparer.Ordinal).ToArray());
        }

        public string GraphId { get; }
        public string SourceRevision { get; }
        public string EncodingContract { get; }
        public string NodeContract { get; }
        public string NumericTarget { get; }
        public string CompilationContract { get; }
        public IReadOnlyList<GraphExecutionDependency> Dependencies { get; }

        [ValueValidation]
        void RequireIdentity()
        {
            SimulationIdentity.Require(GraphId, nameof(GraphId));
            SimulationIdentity.Require(SourceRevision, nameof(SourceRevision));
            SimulationIdentity.Require(EncodingContract, nameof(EncodingContract));
            SimulationIdentity.Require(NodeContract, nameof(NodeContract));
            SimulationIdentity.Require(NumericTarget, nameof(NumericTarget));
            SimulationIdentity.Require(CompilationContract, nameof(CompilationContract));
        }

        public void RequireMatches(GraphCompilationIdentity expected)
        {
            Require("graph", GraphId, expected.GraphId);
            Require("source", SourceRevision, expected.SourceRevision);
            Require("encoding", EncodingContract, expected.EncodingContract);
            Require("nodes", NodeContract, expected.NodeContract);
            Require("numeric", NumericTarget, expected.NumericTarget);
            Require("compilation", CompilationContract, expected.CompilationContract);
            if (Dependencies.Count != expected.Dependencies.Count)
                throw new InvalidDataException($"图 '{GraphId}' 的依赖数量不匹配。");
            for (int i = 0; i < Dependencies.Count; i++)
            {
                Require("dependency", Dependencies[i].Identity, expected.Dependencies[i].Identity);
                Require(Dependencies[i].Identity, Dependencies[i].Revision, expected.Dependencies[i].Revision);
                if (Dependencies[i].Kind != expected.Dependencies[i].Kind)
                    throw new InvalidDataException($"图 '{GraphId}' 的依赖种类不匹配。");
            }
        }

        static void Require(string field, string actual, string expected)
        {
            if (actual != expected)
                throw new InvalidDataException($"图产物 '{field}' 不匹配：'{actual}'，请求 '{expected}'。");
        }
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class GraphExecutionIdentity
    {
        public GraphExecutionIdentity(GraphCompilationIdentity source, StableHash structureIdentity,
            StableHash parameterIdentity, StableHash targetParameterIdentity,
            StableHash stateLayoutIdentity, StableHash sourceIdentity)
        {
            Source = source;
            StructureIdentity = structureIdentity;
            ParameterIdentity = parameterIdentity;
            TargetParameterIdentity = targetParameterIdentity;
            StateLayoutIdentity = stateLayoutIdentity;
            SourceIdentity = sourceIdentity;
            var execution = new List<string>
            {
                "btsmtl-graph-execution/8", source.GraphId, source.EncodingContract, source.NodeContract,
                source.NumericTarget, source.CompilationContract, structureIdentity.Value, parameterIdentity.Value,
                targetParameterIdentity.Value, stateLayoutIdentity.Value
            };
            foreach (GraphExecutionDependency dependency in source.Dependencies)
                if (dependency.Kind == GraphExecutionDependencyKind.RuntimeContent)
                {
                    execution.Add(dependency.Identity);
                    execution.Add(dependency.Revision);
                }
            ExecutionIdentity = StableHash.Compute(execution.ToArray());
            var provenance = new List<string>
            {
                "btsmtl-graph-content/8", ExecutionIdentity.Value, sourceIdentity.Value, source.SourceRevision
            };
            foreach (GraphExecutionDependency dependency in source.Dependencies)
            {
                provenance.Add(((byte)dependency.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture));
                provenance.Add(dependency.Identity);
                provenance.Add(dependency.Revision);
            }
            ContentIdentity = StableHash.Compute(provenance.ToArray());
        }

        public GraphCompilationIdentity Source { get; }
        public StableHash StructureIdentity { get; }
        public StableHash ParameterIdentity { get; }
        public StableHash TargetParameterIdentity { get; }
        public StableHash StateLayoutIdentity { get; }
        public StableHash SourceIdentity { get; }
        [ValueStorageIgnore] public StableHash ExecutionIdentity { get; }
        [ValueStorageIgnore] public StableHash ContentIdentity { get; }

        public void RequireMatches(GraphExecutionIdentity expected)
        {
            if (ContentIdentity != expected.ContentIdentity)
                throw new InvalidDataException($"图 '{Source.GraphId}' 的分块清单与请求内容不匹配。");
        }
    }

}
