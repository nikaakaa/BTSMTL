using System.Security.Cryptography;
using System.Text.Json;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.WebWorkbench;

public sealed record GraphNode(string Id, OrchestrationOperationCode Code, Dictionary<string, bool> Inputs);
public sealed record GraphEdge(string Id, string Source, string Target, string Port);
public sealed record GraphRequest(string Id, string Entry, GraphNode[] Nodes, GraphEdge[] Edges);

public static class GraphCompiler
{
    public static readonly OrchestrationOperationCode[] EditableOperations =
    {
        OrchestrationOperationCode.Root,
        OrchestrationOperationCode.Sequence,
        OrchestrationOperationCode.Selector,
        OrchestrationOperationCode.Parallel,
        OrchestrationOperationCode.Branch,
        OrchestrationOperationCode.ConditionResult,
        OrchestrationOperationCode.Succeed
    };

    public static object Compile(GraphRequest request)
    {
        var report = new SimulationCompileReport();
        try
        {
            RequireTree(request);
            var builder = new BtsmtlGraphSemanticBuilder(report, ProgramStateSchema.Core);
            using var invocation = builder.PushGraphInvocation(request.Id, string.Empty);
            var handles = new Dictionary<string, OperationHandle>();
            foreach (GraphNode node in request.Nodes)
            {
                var source = Source(request.Id, node.Id);
                OperationHandle handle = builder.DeclareOperation(source, node.Code, Array.Empty<int>());
                handles.Add(node.Id, handle);
                foreach (var input in node.Inputs)
                {
                    int constant = builder.DeclareConstant(source, input.Key, input.Value);
                    builder.DeclareConstantInputBinding(handle, input.Key, constant, SemanticValueKind.Boolean, source);
                }
            }
            for (int index = 0; index < request.Edges.Length; index++)
            {
                GraphEdge edge = request.Edges[index];
                builder.DeclareControlFlow(edge.Id, handles[edge.Source], handles[edge.Target],
                    edge.Port, "In", ProgramControlFlowKind.Child, index, 0,
                    OrchestrationAbortPolicy.None, false, OperationHandle.Invalid, Source(request.Id, edge.Source));
            }
            builder.DeclareGraphInvocation(handles[request.Entry], handles[request.Entry],
                Source(request.Id, string.Empty), ProgramInvocationCallerKind.None, string.Empty, string.Empty, -1);
            string revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
            var identity = new GraphExecutionIdentity(request.Id, revision, "web-workbench/1",
                GraphNodeSemanticContract.Identity, Float32SimulationNumericProfile.Value.Id.ToString(),
                Array.Empty<GraphExecutionDependency>());
            var content = builder.BuildOrchestration<Float32OrchestrationValue, Float32Scalar,
                Float32Vector2, Float32Vector3, Float32Yaw, Float32Quaternion,
                Float32OrchestrationValueAccess, BtsmtlFloat32OrchestrationLowering>(
                handles[request.Entry], identity,
                new BtsmtlFloat32OrchestrationLowering(new List<Float32ScalarConversion>()),
                null, null);
            return new
            {
                success = report.IsValid,
                revision,
                compiler = typeof(BtsmtlGraphSemanticBuilder).Assembly.GetName().Name,
                operationCount = content.Program.Operations.Length,
                edgeCount = content.Program.Edges.Length,
                constantCount = content.Program.Constants.Length,
                entryDepth = content.Program.WorkspaceCapacity.EntryDepth,
                operations = content.Program.Operations.ToArray().Select((operation, index) => new
                {
                    node = request.Nodes[index].Id,
                    code = operation.Code.ToString(),
                    operationIndex = index,
                    inputCount = operation.Inputs.Count
                }),
                diagnostics = report.Messages.Select(message => new { code = message.Code, message = message.Message, source = message.SourceIdentity })
            };
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return new
            {
                success = false,
                diagnostics = report.Messages.Count > 0
                    ? report.Messages.Select(message => new { code = message.Code, message = message.Message, source = message.SourceIdentity }).ToArray()
                    : new[] { new { code = "web_graph_input", message = error.Message, source = request.Id } }
            };
        }
    }

    static SimulationSourceLocation Source(string graph, string node) =>
        new("web-workbench", graph, node, "", "", "", graph + "/" + node);

    static void RequireTree(GraphRequest request)
    {
        var nodes = request.Nodes.ToDictionary(node => node.Id);
        if (!nodes.ContainsKey(request.Entry))
            throw new ArgumentException("请选择一个存在的入口节点。");
        var incoming = nodes.Keys.ToDictionary(id => id, _ => 0);
        foreach (GraphNode node in request.Nodes)
        {
            if (!EditableOperations.Contains(node.Code))
                throw new ArgumentException($"首版尚未提供 {node.Code} 的编辑适配。");
        }
        foreach (GraphEdge edge in request.Edges)
        {
            if (!nodes.ContainsKey(edge.Source) || !nodes.ContainsKey(edge.Target))
                throw new ArgumentException($"连线 {edge.Id} 的端点不存在。");
            incoming[edge.Target]++;
        }
        if (incoming[request.Entry] != 0 || incoming.Any(pair => pair.Key != request.Entry && pair.Value != 1))
            throw new ArgumentException("首版使用树形连接：入口没有父节点，其他节点必须连接到一个父节点。");
        var children = request.Edges.ToLookup(edge => edge.Source, edge => edge.Target);
        int reachableCount = 0;
        var pending = new Stack<string>();
        pending.Push(request.Entry);
        while (pending.Count > 0)
        {
            string node = pending.Pop();
            reachableCount++;
            foreach (string child in children[node])
                pending.Push(child);
        }
        if (reachableCount != nodes.Count)
            throw new ArgumentException("存在未连接到入口的节点，请连接后再编译。");
    }
}
