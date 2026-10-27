using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using FlowCanvas;
using FlowCanvas.Macros;
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
        // nodes 由宿主按节点能力提供领域节点的编译描述。
        public static void Compile<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
            BtsmtlGraphRoot root, GraphHostContract contract,
            IOrchestrationGraphContentEncoding<TValue> encoding,
            TLowering lowering,
            IBtsmtlOrchestrationContentEncoder<TValue, TScalar> contentEncoder,
            IBtsmtlGraphNodeCompileSource nodes)
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
                new Compilation(root, contract, identity.SourceRevision, nodes).Build<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
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
            readonly BtsmtlBlackboardCompilation m_Blackboard;
            readonly BtsmtlFlowCompileGraphs m_Graphs;

            internal Compilation(BtsmtlGraphRoot root, GraphHostContract contract, string hash,
                IBtsmtlGraphNodeCompileSource nodes)
            {
                m_Root = root;
                m_Hash = hash;
                m_Route = "graph:" + root.AuthoringId;
                m_Canvas = new BtsmtlGraphCompilation(m_Builder);
                m_Blackboard = CreateBlackboard(contract);
                m_Graphs = new BtsmtlFlowCompileGraphs(
                    node => new BtsmtlFlowCompileNodeInfo(string.Empty, null, compiled => Describe(m_Graphs.Source(compiled), nodes)),
                    graph => graph is BtsmtlMacroGraph macro ? macro.AuthoringId : ((BtsmtlGraphRoot)graph).AuthoringId);
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
                m_Blackboard.CompileDeclarations();
                BtsmtlGraphOperationBindings operations = EmitGraph(m_Graphs.Read(m_Root), m_Root.AuthoringId, m_Route,
                    m_Root.Variables, out _);
                var entryContracts = new Dictionary<string, OrchestrationExecutionContract>(StringComparer.Ordinal);
                foreach (BtsmtlGraphEntryDeclaration entry in m_Root.Entries)
                {
                    m_Builder.DeclareReference(entry.EntryId, OperationHandle.Invalid,
                        ProgramReferenceKind.Operation, operations.Node(entry.NodeId).Value,
                        m_Root.AuthoringId, GraphSource(m_Root, m_Root.AuthoringId, m_Route));
                    entryContracts.Add(entry.EntryId, entry.Contract);
                }
                m_Blackboard.DeclareScopes();
                if (!m_Builder.Report.IsValid)
                    throw new InvalidOperationException($"图 '{m_Root.AuthoringId}' 编译失败：" +
                        string.Join("；", m_Builder.Report.Messages.Select(message => $"{message.Code} {message.SourceIdentity} {message.Message}")));
                OperationHandle owner = operations.Node(m_Root.Entries[0].NodeId);
                m_Builder.DeclareGraphInvocation(owner, owner, GraphSource(m_Root, m_Root.AuthoringId, m_Route),
                    ProgramInvocationCallerKind.None, string.Empty, string.Empty, -1);
                return m_Builder.BuildOrchestration<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
                    owner, identity, lowering, contentEncoder, entryContracts);
            }

            // 图根与每个子图出现共用：叶子、变量绑定、子图调用与连接按所属出现路径发射。
            BtsmtlGraphOperationBindings EmitGraph(CompileGraph graph, string graphId, string route,
                BtsmtlGraphVariableDeclarations variables, out BtsmtlMacroCompilation compiled)
            {
                var operations = new BtsmtlGraphOperationBindings();
                m_Blackboard.BeginGraph(variables.OwnerId, route,
                    variables.Blackboard.variables.Values.Select(value => value.ID).OrderBy(id => id, StringComparer.Ordinal),
                    OperationHandle.Invalid);
                try
                {
                    compiled = graph.IsMacro
                        ? new BtsmtlMacroCompilation(m_Builder, null, graphId, route, m_Hash, graph, operations)
                        : null;
                    foreach (CompileNode node in graph.Nodes)
                    {
                        if (node.Role == CompileNodeRole.MacroInput || node.Role == CompileNodeRole.MacroOutput)
                            continue;
                        if (node.Role == CompileNodeRole.MacroCall)
                            EmitCall(node, graphId, route, operations);
                        else
                            EmitLeaf(node, graphId, route, operations);
                    }
                    foreach (CompileNode node in graph.Nodes)
                        for (int i = 0; i < node.OutEdges.Count; i++)
                        {
                            CompileEdge edge = node.OutEdges[i];
                            int portIndex = edge.IsValue ? 0 : IndexOf(node.FlowOutputs, edge.SourcePortId);
                            m_Canvas.EmitEdge(graphId, route + "/edge:" + edge.Id, m_Hash,
                                edge, operations, ProgramControlFlowKind.Child,
                                portIndex * (node.OutEdges.Count + 1) + i, portIndex,
                                OrchestrationAbortPolicy.None, OperationHandle.Invalid);
                        }
                    m_Canvas.PublishPortSources(graphId, route, m_Hash, graph.Nodes, operations);
                    return operations;
                }
                finally
                {
                    m_Blackboard.EndGraph();
                }
            }

            static int IndexOf(IReadOnlyList<string> ports, string port)
            {
                for (int i = 0; i < ports.Count; i++)
                    if (ports[i] == port)
                        return i;
                return -1;
            }

            void EmitLeaf(CompileNode node, string graphId, string route, BtsmtlGraphOperationBindings operations)
            {
                SimulationNodeEmission emission = node.Describe();
                OperationHandle operation = m_Canvas.EmitLeaf(node, graphId, route, m_Hash, emission);
                operations.AddNode(node, operation);
                SimulationSourceLocation source = BtsmtlGraphCompilation.Source(graphId, route, m_Hash, node);
                if (node.Role == CompileNodeRole.Variable)
                {
                    m_Blackboard.Bind(operation, route, node.VariableOwnerId, node.VariableId, null, source, node.VariableWrites);
                    if (node.VariableWrites)
                    {
                        OperationHandle read = m_Builder.DeclareOperation(source,
                            OrchestrationOperationCode.BlackboardGet, Array.Empty<int>(),
                            text0: node.VariableId, phase: "value");
                        m_Blackboard.Bind(read, route, node.VariableOwnerId, node.VariableId, null, source,
                            binding: BtsmtlGraphVariableReference.CompileIdentity(node.VariableOwnerId, node.VariableId) + "/value");
                        operations.AddValueInput(node, "Value", operation, "m_Value");
                        operations.AddValueOutput(node, "Value", read, "m_Output");
                    }
                    else
                        operations.AddValueOutput(node, "Value", operation, "m_Output");
                    return;
                }
                foreach (CompileValuePort input in node.ValueInputs)
                    operations.AddValueInput(node, input.Id, operation, input.CompiledId);
                foreach (CompileValuePort output in node.ValueOutputs)
                    operations.AddValueOutput(node, output.Id, operation, output.CompiledId);
            }

            // 每个调用位置是一次独立出现：子图体、参数槽与局部变量都按 调用方Route/call:<节点UID> 声明。
            void EmitCall(CompileNode call, string callerGraphId, string callerRoute,
                BtsmtlGraphOperationBindings parent)
            {
                var macro = (BtsmtlMacroGraph)((MacroNodeWrapper)m_Graphs.Source(call)).macro;
                string route = $"{callerRoute}/call:{call.Id}";
                using (m_Builder.PushGraphInvocation(route, m_Builder.CurrentGraphInvocationPath))
                {
                    EmitGraph(call.Callee, macro.AuthoringId, route, macro.Variables, out BtsmtlMacroCompilation compiled);
                    m_Builder.DeclareGraphInvocation(compiled.Entry, compiled.Entry, GraphSource(macro, macro.AuthoringId, route),
                        ProgramInvocationCallerKind.Node, call.Id, string.Empty, -1);
                    compiled.EmitCall(call, callerGraphId, route, m_Hash, parent);
                }
            }

            // Graph 作用域的执行持有者保持未指定，图的各个入口共用同一份值；子图局部变量按出现路径各持一份。
            BtsmtlBlackboardCompilation CreateBlackboard(GraphHostContract contract)
            {
                var sources = new Dictionary<string, BtsmtlBlackboardDeclarationSource>(StringComparer.Ordinal);
                AddDeclarations(sources, m_Root.Variables, m_Route);
                var owners = new Dictionary<string, BtsmtlMacroGraph>(StringComparer.Ordinal);
                CollectMacros(m_Root, m_Route, sources, owners);
                return new BtsmtlBlackboardCompilation(sources, m_Builder, contract.BlackboardScopes, true,
                    (declaration, source, fields) => fields.Add(m_Builder.ConstantField(source, "Public",
                        ((Variable)declaration.AuthoringDeclaration).isExposedPublic)));
            }

            static void CollectMacros(FlowGraph graph, string route,
                Dictionary<string, BtsmtlBlackboardDeclarationSource> sources, Dictionary<string, BtsmtlMacroGraph> owners)
            {
                foreach (MacroNodeWrapper call in BtsmtlFlowCompileGraphs.ReadNodes(graph).OfType<MacroNodeWrapper>())
                {
                    var macro = (BtsmtlMacroGraph)call.macro;
                    string callRoute = $"{route}/call:{call.UID}";
                    BtsmtlGraphVariableDeclarations declarations = macro.Variables;
                    if (owners.TryGetValue(declarations.OwnerId, out BtsmtlMacroGraph existing))
                    {
                        if (existing != macro)
                            throw new InvalidOperationException(
                                $"{callRoute}: 子图 '{macro.name}' 与 '{existing.name}' 的局部变量 owner 身份重复。");
                    }
                    else
                    {
                        owners.Add(declarations.OwnerId, macro);
                        AddDeclarations(sources, declarations, callRoute);
                    }
                    CollectMacros(macro, callRoute, sources, owners);
                }
            }

            static void AddDeclarations(Dictionary<string, BtsmtlBlackboardDeclarationSource> sources,
                BtsmtlGraphVariableDeclarations declarations, string route)
            {
                declarations.Validate();
                string contentHash = StableHash.Compute(declarations.OwnerId, declarations.Revision).Value;
                foreach (Variable variable in declarations.Blackboard.variables.Values.OrderBy(value => value.ID, StringComparer.Ordinal))
                {
                    BtsmtlBlackboardDeclaration declaration = declarations.RequireDeclaration(variable.ID);
                    sources.Add(BtsmtlGraphVariableReference.CompileIdentity(declarations.OwnerId, variable.ID),
                        new BtsmtlBlackboardDeclarationSource(declarations.OwnerId, variable.GetType().FullName, contentHash, variable,
                            new BtsmtlBlackboardDeclarationSnapshot(variable.ID, variable.name,
                                BtsmtlFlowCompileGraphs.ValueType(variable.varType),
                                declaration.Scope, declaration.Lifetime, declaration.Category,
                                ((ISerializedVariableValue)variable).serializedValue, declaration.InputBinding?.InputValueId,
                                declaration.InputBinding?.Projection ?? default),
                            route));
                }
            }

            SimulationSourceLocation GraphSource(FlowGraph graph, string graphId, string route) =>
                new(graph.GetType().FullName, graphId, string.Empty, string.Empty, string.Empty, string.Empty,
                    route, contentHash: m_Hash);
        }

        public static SimulationNodeEmission Describe(FlowNode node, IBtsmtlGraphNodeCompileSource nodes)
        {
            if (BtsmtlNativeValueNodeCatalog.TryGetNode(node.GetType(), out BtsmtlNativeValueNodeDefinition native))
                return new SimulationNodeEmission(native.Code, integer0: native.Variant);
            if (BtsmtlGraphDomainNodes.TryDescribe(GraphOrchestrationNodeDeclaration.Of(node.GetType()),
                    () => nodes.Describe(node), out SimulationNodeEmission domain))
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
