using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using FlowCanvas;
using NodeCanvas.Framework;
using ThirdPersonSimulation;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Graphs.Editor
{
    public static class BtsmtlGraphCompiler
    {
        // 数值类型与 lowering 由宿主按 contract.NumericTarget 选择，须与宿主运行时的数值类型参数一致。
        // contentEncoder 编码图中扩展操作的作者内容，由提供这些扩展领域的宿主给出；图没有扩展操作时传 null。
        public static void Compile<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
            BtsmtlGraphRoot root, GraphHostContract contract,
            IOrchestrationGraphContentEncoding<TValue> encoding,
            TLowering lowering,
            IBtsmtlOrchestrationContentEncoder<TValue, TScalar> contentEncoder)
            where TValue : unmanaged
            where TScalar : unmanaged, IComparable<TScalar>, IEquatable<TScalar>
            where TVector2 : unmanaged
            where TVector3 : unmanaged
            where TYaw : unmanaged
            where TQuaternion : unmanaged
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TLowering : struct, IBtsmtlOrchestrationNumericLowering<TScalar, TVector2, TVector3, TYaw, TQuaternion>
        {
            var fingerprint = new BtsmtlGraphFingerprint();
            GraphExecutionIdentity identity = fingerprint.CreateIdentity(root, root.AuthoringId,
                encoding.EncodingContract, contract.NumericTarget);
            string inputIdentity = ComputeCompilationInputIdentity(contract,
                StableHash.Compute(encoding.WriteIdentity(identity)).Value,
                typeof(TLowering).FullName, typeof(TValues).FullName, contentEncoder?.ContractIdentity ?? string.Empty);
            if (root.CompilationInputIdentity == inputIdentity)
            {
                Debug.Log($"Graph '{root.AuthoringId}' is up to date; validation, compilation and asset writes skipped.");
                return;
            }
            BtsmtlGraphValidator.Require(root, contract);
            root.SelfSerialize();
            OrchestrationGraphContent<TValue> content =
                new Compilation(root, contract, identity.SourceRevision).Build<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
                    identity, lowering, contentEncoder);
            root.SetExecutionData(encoding.Write(content), identity, encoding, inputIdentity);
            EditorUtility.SetDirty(root);
        }

        static string ComputeCompilationInputIdentity(GraphHostContract contract, string sourceIdentity,
            string loweringType, string valueAccessType, string contentContract)
        {
            var parts = new List<string>
            {
                "btsmtl-graph-compiler/2", sourceIdentity, loweringType, valueAccessType, contentContract,
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

        sealed class UnitySemanticBuilder : BtsmtlGraphSemanticBuilder
        {
            internal UnitySemanticBuilder() : base(new SimulationCompileReport(), ProgramStateSchema.Core) { }
            protected override SemanticLiteral CreateLiteral(int index, string identity, object value) =>
                CreateCoreLiteral(index, identity, value switch
                {
                    Vector2 vector => new SemanticVector2Value(vector.x, vector.y),
                    Vector3 vector => new SemanticVector3Value(vector.x, vector.y, vector.z),
                    Quaternion rotation => new SemanticQuaternionValue(rotation.x, rotation.y, rotation.z, rotation.w),
                    _ => value
                });
        }

        sealed class Compilation
        {
            readonly BtsmtlGraphRoot m_Root;
            readonly string m_Hash;
            readonly string m_Route;
            readonly UnitySemanticBuilder m_Builder = new();
            readonly BtsmtlGraphCompilation m_Canvas;
            readonly BtsmtlGraphOperationBindings m_Operations = new();
            readonly BtsmtlBlackboardCompilation m_Blackboard;

            internal Compilation(BtsmtlGraphRoot root, GraphHostContract contract, string hash)
            {
                m_Root = root;
                m_Hash = hash;
                m_Route = "graph:" + root.AuthoringId;
                m_Canvas = new BtsmtlGraphCompilation(m_Builder);
                m_Blackboard = CreateBlackboard(contract);
            }

            internal OrchestrationGraphContent<TValue>
                Build<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(GraphExecutionIdentity identity, TLowering lowering,
                    IBtsmtlOrchestrationContentEncoder<TValue, TScalar> contentEncoder)
                where TValue : unmanaged
                where TScalar : unmanaged, IComparable<TScalar>, IEquatable<TScalar>
                where TVector2 : unmanaged
                where TVector3 : unmanaged
                where TYaw : unmanaged
                where TQuaternion : unmanaged
                where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
                where TLowering : struct, IBtsmtlOrchestrationNumericLowering<TScalar, TVector2, TVector3, TYaw, TQuaternion>
            {
                using var invocation = m_Builder.PushGraphInvocation(m_Route, string.Empty);
                DeclareVariables();
                FlowNode[] nodes = BtsmtlGraphCompilation.ReadNodes(m_Root);
                foreach (FlowNode node in nodes)
                {
                    SimulationNodeEmission emission = Describe(node, m_Builder.Report);
                    OperationHandle operation = m_Canvas.EmitLeaf(node, m_Root.AuthoringId, m_Route,
                        m_Hash, emission, Describe);
                    m_Operations.AddNode(node, operation);
                    BtsmtlNativeValueNodeCatalog.TryGetNode(node.GetType(), out BtsmtlNativeValueNodeDefinition native);
                    if (node is IBtsmtlVariableFlowNode variable)
                    {
                        string ownerId = m_Root.Variables.OwnerId;
                        m_Blackboard.Bind(operation, m_Route, ownerId, variable.VariableId, null, Source(node), variable.Writes);
                        if (variable.Writes)
                        {
                            OperationHandle read = m_Builder.DeclareOperation(Source(node),
                                OrchestrationOperationCode.BlackboardGet, Array.Empty<int>(),
                                text0: variable.VariableId, phase: "value");
                            m_Blackboard.Bind(read, m_Route, ownerId, variable.VariableId, null, Source(node),
                                binding: BtsmtlGraphVariableReference.CompileIdentity(ownerId, variable.VariableId) + "/value");
                            m_Operations.AddValueInput(node, "Value", operation, "m_Value");
                            m_Operations.AddValueOutput(node, "Value", read, "m_Output");
                        }
                        else
                            m_Operations.AddValueOutput(node, "Value", operation, "m_Output");
                    }
                    else
                    {
                        foreach (ValueInput input in node.GetInputValuePorts())
                            m_Operations.AddValueInput(node, input.ID, operation,
                                BtsmtlGraphCompilation.Input(node, input.ID, native));
                        foreach (ValueOutput output in node.GetOutputValuePorts())
                            m_Operations.AddValueOutput(node, output.ID, operation,
                                BtsmtlGraphCompilation.Output(node, output.ID, native, emission.Code));
                    }
                }
                foreach (FlowNode node in nodes)
                {
                    FlowOutput[] ports = node.GetOutputFlowPorts().ToArray();
                    for (int i = 0; i < node.outConnections.Count; i++)
                    {
                        var edge = (BinderConnection)node.outConnections[i];
                        int portIndex = edge.sourcePort is FlowOutput flow ? Array.IndexOf(ports, flow) : 0;
                        m_Canvas.EmitEdge(m_Root.AuthoringId, m_Route + "/edge:" + edge.UID, m_Hash,
                            edge, m_Operations, ProgramControlFlowKind.Child,
                            portIndex * (node.outConnections.Count + 1) + i, portIndex,
                            OrchestrationAbortPolicy.None, OperationHandle.Invalid);
                    }
                }
                m_Canvas.PublishPortSources(m_Root.AuthoringId, m_Route, m_Hash, nodes, m_Operations);
                var entryContracts = new Dictionary<string, OrchestrationExecutionContract>(StringComparer.Ordinal);
                foreach (BtsmtlGraphEntryDeclaration entry in m_Root.Entries)
                {
                    m_Builder.DeclareReference(entry.EntryId, OperationHandle.Invalid,
                        ProgramReferenceKind.Operation, m_Operations.Node(entry.NodeId).Value,
                        m_Root.AuthoringId, GraphSource());
                    entryContracts.Add(entry.EntryId, entry.Contract);
                }
                m_Blackboard.EndGraph();
                m_Blackboard.DeclareScopes();
                if (!m_Builder.Report.IsValid)
                    throw new InvalidOperationException($"图 '{m_Root.AuthoringId}' 编译失败：" +
                        string.Join("；", m_Builder.Report.Messages.Select(message => $"{message.Code} {message.SourceIdentity} {message.Message}")));
                OperationHandle owner = m_Operations.Node(m_Root.Entries[0].NodeId);
                m_Builder.DeclareGraphInvocation(owner, owner, GraphSource(), ProgramInvocationCallerKind.None,
                    string.Empty, string.Empty, -1);
                return m_Builder.BuildOrchestration<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
                    owner, identity, lowering, contentEncoder, entryContracts);
            }

            // Graph 作用域的执行持有者保持未指定，图的各个入口共用同一份值。
            BtsmtlBlackboardCompilation CreateBlackboard(GraphHostContract contract)
            {
                BtsmtlGraphVariableDeclarations declarations = m_Root.Variables;
                declarations.Validate();
                string contentHash = StableHash.Compute(declarations.OwnerId, declarations.Revision).Value;
                var sources = new Dictionary<string, BtsmtlBlackboardDeclarationSource>(StringComparer.Ordinal);
                foreach (Variable variable in declarations.Blackboard.variables.Values.OrderBy(value => value.ID, StringComparer.Ordinal))
                {
                    BtsmtlBlackboardDeclaration declaration = declarations.RequireDeclaration(variable.ID);
                    sources.Add(BtsmtlGraphVariableReference.CompileIdentity(declarations.OwnerId, variable.ID),
                        new BtsmtlBlackboardDeclarationSource(declarations.OwnerId, variable.GetType().FullName, contentHash, variable,
                            new BtsmtlBlackboardDeclarationSnapshot(variable.ID, variable.name, variable.varType,
                                declaration.Scope, declaration.Lifetime, declaration.Category,
                                ((ISerializedVariableValue)variable).serializedValue, declaration.InputBinding),
                            m_Route));
                }
                return new BtsmtlBlackboardCompilation(sources, m_Builder, contract.BlackboardScopes, true,
                    (declaration, source, fields) => fields.Add(m_Builder.ConstantField(source, "Public",
                        ((Variable)declaration.AuthoringDeclaration).isExposedPublic)));
            }

            void DeclareVariables()
            {
                m_Blackboard.CompileDeclarations();
                m_Blackboard.BeginGraph(m_Root.Variables.OwnerId, m_Route,
                    m_Root.Variables.Blackboard.variables.Values.Select(value => value.ID).OrderBy(id => id, StringComparer.Ordinal),
                    OperationHandle.Invalid);
            }

            SimulationSourceLocation Source(FlowNode node) =>
                BtsmtlGraphCompilation.Source(m_Root.AuthoringId, m_Route, m_Hash, node);
            SimulationSourceLocation GraphSource() => new(m_Root.GetType().FullName, m_Root.AuthoringId,
                string.Empty, string.Empty, string.Empty, string.Empty, m_Route, contentHash: m_Hash);
        }

        public static SimulationNodeEmission Describe(FlowNode node, SimulationCompileReport report)
        {
            if (BtsmtlNativeValueNodeCatalog.TryGetNode(node.GetType(), out BtsmtlNativeValueNodeDefinition native))
                return new SimulationNodeEmission(native.Code, integer0: native.Variant);
            if (BtsmtlGraphDomainNodes.TryDescribe(node, out SimulationNodeEmission domain))
                return domain;
            if (node is IBtsmtlHostInputFlowNode input)
                return new SimulationNodeEmission(OrchestrationOperationCode.HostInput,
                    hostInputs: new[] { new SemanticHostInputPort("m_Output", input.InputId,
                        BtsmtlGraphValueTypes.Kind(input.ValueType), GraphCompiledValues.TypeIdentity(input.ValueType)) });
            if (node is IBtsmtlVariableFlowNode variable)
                return new SimulationNodeEmission(variable.Writes
                    ? OrchestrationOperationCode.BlackboardSet : OrchestrationOperationCode.BlackboardGet,
                    text0: variable.VariableId);
            var operation = (BtsmtlGraphOperationAttribute)Attribute.GetCustomAttribute(
                node.GetType(), typeof(BtsmtlGraphOperationAttribute), false);
            return new SimulationNodeEmission(operation.Code,
                integer0: node is BtsmtlParallelFlowNode parallel ? (int)parallel.Mode : 0);
        }
    }
}
