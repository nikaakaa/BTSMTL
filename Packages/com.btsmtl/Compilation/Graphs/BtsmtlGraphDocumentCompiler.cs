using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Blackboard;
using BTSMTL.Authoring.Graph;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    public sealed class BtsmtlGraphDocumentCompiler
    {
        readonly BtsmtlGraphDocument m_Root;
        readonly BtsmtlGraphDocumentViews m_Graphs;
        readonly Func<string, BtsmtlGraphVariableDeclarations> m_Variables;
        readonly string m_Hash;
        readonly string m_Route;
        readonly BtsmtlGraphSemanticBuilder m_Builder;
        readonly BtsmtlGraphCompilation m_Canvas;
        readonly BtsmtlBlackboardCompilation m_Blackboard;

        public BtsmtlGraphDocumentCompiler(BtsmtlGraphDocument root, BtsmtlGraphDocumentViews graphs,
            Func<string, BtsmtlGraphVariableDeclarations> variables,
            IReadOnlyList<PipelineBlackboardVariableScope> scopes, string sourceRevision,
            SimulationCompileReport report)
        {
            m_Root = root;
            m_Graphs = graphs;
            m_Variables = variables;
            m_Hash = sourceRevision;
            m_Route = "graph:" + root.AuthoringId;
            m_Builder = new BtsmtlGraphSemanticBuilder(report, ProgramStateSchema.Core);
            m_Canvas = new BtsmtlGraphCompilation(m_Builder);
            RequireEntries(root, graphs.Read(root));
            var sources = new Dictionary<string, BtsmtlBlackboardDeclarationSource>(StringComparer.Ordinal);
            CollectDeclarations(root, m_Route, sources, new Dictionary<string, string>(StringComparer.Ordinal));
            m_Blackboard = new BtsmtlBlackboardCompilation(sources, m_Builder, scopes,
                (declaration, source, fields) => fields.Add(m_Builder.ConstantField(source, "Public",
                    ((BtsmtlGraphVariableDeclaration)declaration.AuthoringDeclaration).IsPublic)));
        }

        public OrchestrationGraphContent<TValue>
            Build<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
                GraphCompilationIdentity identity, TLowering lowering,
                IBtsmtlOrchestrationContentEncoder<TValue, TScalar> contentEncoder,
                IOrchestrationGraphContentEncoding<TValue> encoding,
                Func<SemanticGraphParameters, StableHash> parameterIdentity)
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
            BtsmtlGraphOperationBindings operations = EmitGraph(m_Root, m_Graphs.Read(m_Root), m_Route, out _);
            var entries = new Dictionary<string, OrchestrationExecutionContract>(StringComparer.Ordinal);
            foreach (BtsmtlGraphEntryDeclaration entry in m_Root.Entries)
            {
                m_Builder.DeclareReference(entry.EntryId, OperationHandle.Invalid, ProgramReferenceKind.Operation,
                    operations.Node(entry.NodeId).Value, m_Root.AuthoringId, GraphSource(m_Root, m_Route));
                entries.Add(entry.EntryId, entry.Contract);
            }
            m_Blackboard.DeclareScopes();
            if (!m_Builder.Report.IsValid)
                throw new InvalidOperationException($"图 '{m_Root.AuthoringId}' 编译失败：" + string.Join("；",
                    m_Builder.Report.Messages.Select(message => $"{message.Code} {message.SourceIdentity} {message.Message}")));
            OperationHandle owner = operations.Node(m_Root.Entries[0].NodeId);
            m_Builder.DeclareGraphInvocation(owner, owner, GraphSource(m_Root, m_Route),
                ProgramInvocationCallerKind.None, string.Empty, string.Empty, -1);
            return m_Builder.BuildOrchestration<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>(
                owner, identity, lowering,
                new BtsmtlOrchestrationCompilationDomain<TValue, TScalar>(ProgramStateSchema.Core, contentEncoder,
                    null, encoding, parameterIdentity), OrchestrationExecutionContract.Complete, entries);
        }

        BtsmtlGraphOperationBindings EmitGraph(BtsmtlGraphDocument document, CompileGraph graph, string route,
            out BtsmtlMacroCompilation compiled)
        {
            var operations = new BtsmtlGraphOperationBindings();
            BtsmtlGraphVariableDeclarations variables = m_Variables(document.VariableOwnerId);
            m_Blackboard.BeginGraph(variables.OwnerId, route,
                variables.BlackboardDeclarations.Select(value => value.VariableId).ToArray(), OperationHandle.Invalid);
            try
            {
                compiled = graph.IsMacro ? new BtsmtlMacroCompilation(m_Builder, null, document.AuthoringId,
                    route, m_Hash, graph, operations) : null;
                foreach (CompileNode node in graph.Nodes)
                    if (node.Role == CompileNodeRole.MacroCall)
                        EmitCall(node, document.AuthoringId, route, operations);
                    else if (node.Role != CompileNodeRole.MacroInput && node.Role != CompileNodeRole.MacroOutput)
                        EmitLeaf(node, document.AuthoringId, route, operations);
                foreach (CompileNode node in graph.Nodes)
                    for (int index = 0; index < node.OutEdges.Count; index++)
                    {
                        CompileEdge edge = node.OutEdges[index];
                        int port = edge.IsValue ? 0 : IndexOf(node.FlowOutputs, edge.SourcePortId);
                        m_Canvas.EmitEdge(document.AuthoringId, route + "/edge:" + edge.Id, m_Hash,
                            edge, operations, ProgramControlFlowKind.Child, port * (node.OutEdges.Count + 1) + index,
                            port, OrchestrationAbortPolicy.None, OperationHandle.Invalid);
                    }
                m_Canvas.PublishPortSources(document.AuthoringId, route, m_Hash, graph.Nodes, operations);
                return operations;
            }
            finally
            {
                m_Blackboard.EndGraph();
            }
        }

        void EmitLeaf(CompileNode node, string graphId, string route, BtsmtlGraphOperationBindings operations)
        {
            OperationHandle operation = m_Canvas.EmitLeaf(node, graphId, route, m_Hash, node.Describe());
            operations.AddNode(node, operation);
            SimulationSourceLocation source = BtsmtlGraphCompilation.Source(graphId, route, m_Hash, node);
            if (node.Role == CompileNodeRole.Variable)
            {
                m_Blackboard.Bind(operation, route, node.VariableOwnerId, node.VariableId, null, source, node.VariableWrites);
                if (node.VariableWrites)
                {
                    OperationHandle read = m_Builder.DeclareOperation(source, OrchestrationOperationCode.BlackboardGet,
                        Array.Empty<int>(), text0: node.VariableId, phase: "value");
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

        void EmitCall(CompileNode call, string callerGraphId, string callerRoute, BtsmtlGraphOperationBindings parent)
        {
            BtsmtlGraphDocument macro = m_Graphs.Document(call.Callee.Id);
            string route = $"{callerRoute}/call:{call.Id}";
            using (m_Builder.PushGraphInvocation(route, m_Builder.CurrentGraphInvocationPath))
            {
                EmitGraph(macro, call.Callee, route, out BtsmtlMacroCompilation compiled);
                m_Builder.DeclareGraphInvocation(compiled.Entry, compiled.Entry, GraphSource(macro, route),
                    ProgramInvocationCallerKind.Node, call.Id, string.Empty, -1);
                compiled.EmitCall(call, callerGraphId, route, m_Hash, parent);
            }
        }

        void CollectDeclarations(BtsmtlGraphDocument document, string route,
            Dictionary<string, BtsmtlBlackboardDeclarationSource> sources, Dictionary<string, string> owners)
        {
            BtsmtlGraphVariableDeclarations variables = m_Variables(document.VariableOwnerId);
            if (owners.TryGetValue(variables.OwnerId, out string graphId))
            {
                if (graphId != document.AuthoringId)
                    throw new InvalidOperationException($"{route}: 图 '{document.AuthoringId}' 与 '{graphId}' 的变量 owner 身份重复。");
            }
            else
            {
                if (document.IsMacro && variables.Variables.Any(variable => variable.IsPublic ||
                        variable.Declaration.Scope != PipelineBlackboardVariableScope.Graph ||
                        variable.Declaration.Lifetime != PipelineBlackboardVariableLifetime.GraphInstance))
                    throw new InvalidOperationException($"{route}: 子图变量必须为不发布的图作用域、图实例生命周期变量。");
                owners.Add(variables.OwnerId, document.AuthoringId);
                foreach (BtsmtlBlackboardDeclarationSource declaration in BtsmtlGraphVariableCompilation.Declarations(variables, route))
                    sources.Add(BtsmtlGraphVariableReference.CompileIdentity(variables.OwnerId,
                        declaration.Declaration.DeclarationId), declaration);
            }
            foreach (CompileNode call in m_Graphs.Read(document).Nodes.Where(node => node.Role == CompileNodeRole.MacroCall))
                CollectDeclarations(m_Graphs.Document(call.Callee.Id), $"{route}/call:{call.Id}", sources, owners);
        }

        SimulationSourceLocation GraphSource(BtsmtlGraphDocument document, string route) =>
            new(BtsmtlGraphDocumentViews.SourceType(document), document.AuthoringId, string.Empty, string.Empty,
                string.Empty, string.Empty, route, contentHash: m_Hash);

        static int IndexOf(IReadOnlyList<string> ports, string port)
        {
            for (int index = 0; index < ports.Count; index++)
                if (ports[index] == port)
                    return index;
            return -1;
        }

        static void RequireEntries(BtsmtlGraphDocument root, CompileGraph graph)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var nodes = new HashSet<string>(StringComparer.Ordinal);
            string[] roots = graph.Nodes.Where(node => node.NodeType != null &&
                    Attribute.GetCustomAttribute(node.NodeType, typeof(BtsmtlGraphOperationAttribute), false)
                        is BtsmtlGraphOperationAttribute operation && operation.Code == OrchestrationOperationCode.Root)
                .Select(node => node.Id).ToArray();
            foreach (BtsmtlGraphEntryDeclaration entry in root.Entries)
                if (string.IsNullOrWhiteSpace(entry.EntryId) || !ids.Add(entry.EntryId) || !nodes.Add(entry.NodeId) ||
                    !roots.Contains(entry.NodeId) || !Enum.IsDefined(typeof(OrchestrationExecutionContract), entry.Contract))
                    throw new InvalidOperationException($"图入口 '{entry.EntryId}' 没有唯一节点和有效的执行合同。");
            if (ids.Count == 0 || roots.Any(id => !nodes.Contains(id)))
                throw new InvalidOperationException("每个图入口节点必须由根声明唯一的执行入口。");
        }
    }
}
