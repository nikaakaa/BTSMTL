using System;
using System.Collections.Generic;
using System.Linq;
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
                m_Blackboard.CompileDeclarations();
                BtsmtlGraphOperationBindings operations = EmitGraph(m_Root, m_Root.AuthoringId, m_Route,
                    m_Root.Variables, null, out _);
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
            BtsmtlGraphOperationBindings EmitGraph(FlowGraph graph, string graphId, string route,
                BtsmtlGraphVariableDeclarations variables, BtsmtlMacroGraph macro, out BtsmtlMacroCompilation compiled)
            {
                var operations = new BtsmtlGraphOperationBindings();
                m_Blackboard.BeginGraph(variables.OwnerId, route,
                    variables.Blackboard.variables.Values.Select(value => value.ID).OrderBy(id => id, StringComparer.Ordinal),
                    OperationHandle.Invalid);
                try
                {
                    compiled = macro != null
                        ? new BtsmtlMacroCompilation(m_Builder, null, graphId, route, m_Hash, macro, operations)
                        : null;
                    FlowNode[] nodes = BtsmtlGraphCompilation.ReadNodes(graph);
                    foreach (FlowNode node in nodes)
                    {
                        if (node is MacroInputNode || node is MacroOutputNode)
                            continue;
                        if (node is MacroNodeWrapper call)
                            EmitCall(call, graphId, route, operations);
                        else
                            EmitLeaf(node, graphId, route, operations);
                    }
                    foreach (FlowNode node in nodes)
                    {
                        FlowOutput[] ports = node.GetOutputFlowPorts().ToArray();
                        for (int i = 0; i < node.outConnections.Count; i++)
                        {
                            var edge = (BinderConnection)node.outConnections[i];
                            int portIndex = edge.sourcePort is FlowOutput flow ? Array.IndexOf(ports, flow) : 0;
                            m_Canvas.EmitEdge(graphId, route + "/edge:" + edge.UID, m_Hash,
                                edge, operations, ProgramControlFlowKind.Child,
                                portIndex * (node.outConnections.Count + 1) + i, portIndex,
                                OrchestrationAbortPolicy.None, OperationHandle.Invalid);
                        }
                    }
                    m_Canvas.PublishPortSources(graphId, route, m_Hash, nodes, operations);
                    return operations;
                }
                finally
                {
                    m_Blackboard.EndGraph();
                }
            }

            void EmitLeaf(FlowNode node, string graphId, string route, BtsmtlGraphOperationBindings operations)
            {
                SimulationNodeEmission emission = Describe(node, m_Builder.Report);
                OperationHandle operation = m_Canvas.EmitLeaf(node, graphId, route, m_Hash, emission, Describe);
                operations.AddNode(node, operation);
                BtsmtlNativeValueNodeCatalog.TryGetNode(node.GetType(), out BtsmtlNativeValueNodeDefinition native);
                SimulationSourceLocation source = BtsmtlGraphCompilation.Source(graphId, route, m_Hash, node);
                if (node is IBtsmtlVariableFlowNode variable)
                {
                    m_Blackboard.Bind(operation, route, variable.OwnerId, variable.VariableId, null, source, variable.Writes);
                    if (variable.Writes)
                    {
                        OperationHandle read = m_Builder.DeclareOperation(source,
                            OrchestrationOperationCode.BlackboardGet, Array.Empty<int>(),
                            text0: variable.VariableId, phase: "value");
                        m_Blackboard.Bind(read, route, variable.OwnerId, variable.VariableId, null, source,
                            binding: BtsmtlGraphVariableReference.CompileIdentity(variable.OwnerId, variable.VariableId) + "/value");
                        operations.AddValueInput(node, "Value", operation, "m_Value");
                        operations.AddValueOutput(node, "Value", read, "m_Output");
                    }
                    else
                        operations.AddValueOutput(node, "Value", operation, "m_Output");
                    return;
                }
                foreach (ValueInput input in node.GetInputValuePorts())
                    operations.AddValueInput(node, input.ID, operation,
                        BtsmtlGraphCompilation.Input(node, input.ID, native));
                foreach (ValueOutput output in node.GetOutputValuePorts())
                    operations.AddValueOutput(node, output.ID, operation,
                        BtsmtlGraphCompilation.Output(node, output.ID, native, emission.Code));
            }

            // 每个调用位置是一次独立出现：子图体、参数槽与局部变量都按 调用方Route/call:<节点UID> 声明。
            void EmitCall(MacroNodeWrapper call, string callerGraphId, string callerRoute,
                BtsmtlGraphOperationBindings parent)
            {
                var macro = (BtsmtlMacroGraph)call.macro;
                string route = $"{callerRoute}/call:{call.UID}";
                using (m_Builder.PushGraphInvocation(route, m_Builder.CurrentGraphInvocationPath))
                {
                    EmitGraph(macro, macro.AuthoringId, route, macro.Variables, macro, out BtsmtlMacroCompilation compiled);
                    m_Builder.DeclareGraphInvocation(compiled.Entry, compiled.Entry, GraphSource(macro, macro.AuthoringId, route),
                        ProgramInvocationCallerKind.Node, call.UID, string.Empty, -1);
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
                foreach (MacroNodeWrapper call in BtsmtlGraphCompilation.ReadNodes(graph).OfType<MacroNodeWrapper>())
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
                            new BtsmtlBlackboardDeclarationSnapshot(variable.ID, variable.name, variable.varType,
                                declaration.Scope, declaration.Lifetime, declaration.Category,
                                ((ISerializedVariableValue)variable).serializedValue, declaration.InputBinding),
                            route));
                }
            }

            SimulationSourceLocation GraphSource(FlowGraph graph, string graphId, string route) =>
                new(graph.GetType().FullName, graphId, string.Empty, string.Empty, string.Empty, string.Empty,
                    route, contentHash: m_Hash);
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
