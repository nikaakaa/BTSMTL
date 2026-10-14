using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ThirdPersonSimulation
{
    public static class GraphNodeSemanticContract
    {
        public const string Identity = "btsmtl-operations/2";
    }

    public readonly partial struct GraphExecutionDependency
    {
        public GraphExecutionDependency(string identity, string revision)
        {
            Identity = SimulationIdentity.Require(identity, nameof(identity));
            Revision = SimulationIdentity.Require(revision, nameof(revision));
        }

        public string Identity { get; }
        public string Revision { get; }

        [ValueValidation]
        void RequireIdentity()
        {
            SimulationIdentity.Require(Identity, nameof(Identity));
            SimulationIdentity.Require(Revision, nameof(Revision));
        }
    }

    public sealed partial class GraphExecutionIdentity
    {
        public GraphExecutionIdentity(string graphId, string sourceRevision, string encodingContract,
            string nodeContract, string numericTarget, IReadOnlyList<GraphExecutionDependency> dependencies)
        {
            GraphId = SimulationIdentity.Require(graphId, nameof(graphId));
            SourceRevision = SimulationIdentity.Require(sourceRevision, nameof(sourceRevision));
            EncodingContract = SimulationIdentity.Require(encodingContract, nameof(encodingContract));
            NodeContract = SimulationIdentity.Require(nodeContract, nameof(nodeContract));
            NumericTarget = SimulationIdentity.Require(numericTarget, nameof(numericTarget));
            Dependencies = Array.AsReadOnly(dependencies.OrderBy(value => value.Identity, StringComparer.Ordinal).ToArray());
        }

        public string GraphId { get; }
        public string SourceRevision { get; }
        public string EncodingContract { get; }
        public string NodeContract { get; }
        public string NumericTarget { get; }
        public IReadOnlyList<GraphExecutionDependency> Dependencies { get; }

        [ValueValidation]
        void RequireIdentity()
        {
            SimulationIdentity.Require(GraphId, nameof(GraphId));
            SimulationIdentity.Require(SourceRevision, nameof(SourceRevision));
            SimulationIdentity.Require(EncodingContract, nameof(EncodingContract));
            SimulationIdentity.Require(NodeContract, nameof(NodeContract));
            SimulationIdentity.Require(NumericTarget, nameof(NumericTarget));
        }

        public void RequireMatches(GraphExecutionIdentity expected)
        {
            Require("graph", GraphId, expected.GraphId);
            Require("source", SourceRevision, expected.SourceRevision);
            Require("encoding", EncodingContract, expected.EncodingContract);
            Require("nodes", NodeContract, expected.NodeContract);
            Require("numeric", NumericTarget, expected.NumericTarget);
            if (Dependencies.Count != expected.Dependencies.Count)
                throw new InvalidDataException($"图 '{GraphId}' 的依赖数量不匹配。");
            for (int i = 0; i < Dependencies.Count; i++)
            {
                Require("dependency", Dependencies[i].Identity, expected.Dependencies[i].Identity);
                Require(Dependencies[i].Identity, Dependencies[i].Revision, expected.Dependencies[i].Revision);
            }
        }

        static void Require(string field, string actual, string expected)
        {
            if (actual != expected)
                throw new InvalidDataException($"图产物 '{field}' 不匹配：'{actual}'，请求 '{expected}'。");
        }
    }

}
