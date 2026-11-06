using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Graphs.Editor
{
    public static class BtsmtlGraphCompiler
    {
        public static void Compile<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
            BtsmtlGraphRoot root, GraphHostContract contract,
            IOrchestrationGraphContentEncoding<TValue> encoding, TLowering lowering,
            IBtsmtlOrchestrationContentEncoder<TValue, TScalar> contentEncoder,
            IBtsmtlGraphNodeCompileSource nodes, Func<SemanticGraphParameters, StableHash> parameterIdentity,
            Action<OrchestrationGraphContent<TValue>, string> publish)
            where TValue : unmanaged
            where TScalar : unmanaged, IComparable<TScalar>, IEquatable<TScalar>
            where TVector2 : unmanaged
            where TVector3 : unmanaged
            where TYaw : unmanaged
            where TQuaternion : unmanaged
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TLowering : struct, IBtsmtlOrchestrationNumericLowering<TScalar, TVector2, TVector3, TYaw, TQuaternion>
        {
            BtsmtlGraphDocumentEditor editor = root.Authoring.Document;
            var fingerprint = new BtsmtlGraphFingerprint();
            GraphCompilationIdentity identity = fingerprint.CreateIdentity(root, root.AuthoringId,
                encoding.EncodingContract, contract.NumericTarget);
            string inputIdentity = CompilationInputIdentity(contract,
                StableHash.Compute(new[] { identity.GraphId, identity.SourceRevision, identity.EncodingContract,
                    identity.NodeContract, identity.NumericTarget }.Concat(identity.Dependencies.SelectMany(
                    dependency => new[] { dependency.Identity, dependency.Revision })).ToArray()).Value,
                typeof(TLowering).FullName, typeof(TValues).FullName, contentEncoder?.ContractIdentity ?? string.Empty,
                encoding.EncodingIdentity.Value);
            if (root.CompilationInputIdentity == inputIdentity)
            {
                Debug.Log($"Graph '{root.AuthoringId}' is up to date; validation, compilation and asset writes skipped.");
                return;
            }
            BtsmtlGraphValidator.Require(root, contract);
            var views = new BtsmtlGraphDocumentViews(editor.Document, editor.Definition,
                (source, definition) => new GraphNodeFields(
                    new BtsmtlGraphJsonFields(source.Json(), definition.GetType(), editor.ReadValue), nodes),
                nodes.Describe);
            var compiler = new BtsmtlGraphDocumentCompiler(root.Source, views, owner =>
                owner == root.Variables.OwnerId ? root.Variables : editor.Documents
                    .Where(document => document.IsMacro && document.VariableOwnerId == owner)
                    .Select(document => ((BtsmtlMacroGraph)editor.Graph(document.AuthoringId)).Variables).Single(),
                contract.BlackboardScopes, identity.SourceRevision, new SimulationCompileReport());
            OrchestrationGraphContent<TValue> content =
                compiler.Build<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
                    identity, lowering, contentEncoder, encoding, parameterIdentity);
            foreach (ref readonly OrchestrationGraphHostInput input in content.Structure.HostInputs.Span)
            {
                string id = content.Identities.Resolve(input.InputIdentityId);
                Type type = GraphCompiledValues.ResolveTypeIdentity(content.Identities.Resolve(input.ValueTypeIdentityId));
                if (!contract.TryGetInput(id, out GraphHostInputDescriptor descriptor) || descriptor.ValueType != type)
                    throw new InvalidOperationException($"输入 '{id}' 未由宿主以相同类型声明。");
            }
            publish(content, inputIdentity);
        }

        static string CompilationInputIdentity(GraphHostContract contract, string sourceIdentity,
            string loweringType, string valueAccessType, string contentContract, string encodingIdentity)
        {
            var parts = new List<string>
            {
                "btsmtl-graph-compiler/6", sourceIdentity, loweringType, valueAccessType, contentContract, encodingIdentity,
                contract.ContractId, contract.Revision, contract.UpdateEventId, contract.NumericTarget
            };
            foreach (GraphHostInputDescriptor input in contract.Inputs)
            {
                parts.Add(input.InputId);
                parts.Add(input.ValueType.AssemblyQualifiedName);
            }
            foreach (GraphHostOutputDescriptor output in contract.Outputs)
            {
                parts.Add(output.VariableId);
                parts.Add(output.ValueType.AssemblyQualifiedName);
            }
            parts.AddRange(contract.BlackboardScopes.Select(scope => scope.ToString()));
            parts.AddRange(contract.Domains);
            return StableHash.Compute(parts.ToArray()).Value;
        }
    }
}
