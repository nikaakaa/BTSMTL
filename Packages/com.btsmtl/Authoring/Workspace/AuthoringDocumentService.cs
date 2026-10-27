#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTSMTL.Authoring.Documents;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Workspace
{
public sealed class AuthoringBaselineConflictException : InvalidOperationException
{
    public AuthoringBaselineConflictException(string rootId, string expected, string current)
        : base($"作者根 {rootId} 基线已过期：请求 {expected}，当前 {current}。")
    { RootId = rootId; ExpectedRevision = expected; CurrentRevision = current; }
    public string RootId { get; }
    public string ExpectedRevision { get; }
    public string CurrentRevision { get; }
}

public sealed record AuthoringDocumentState(string RootPath, string Revision,
    AuthoringDocumentSet? Documents, IReadOnlyList<AuthoringSourceProblem> Problems)
{
    public bool Adoptable => Documents != null && Problems.Count == 0;
}

public sealed class AuthoringDocumentService
{
    readonly AuthoringWorkspaceDirectory m_Workspace;
    readonly IAuthoringDocumentDomain m_Domain;
    readonly AuthoringDocumentCommit m_Commit = new();
    readonly ConcurrentDictionary<string, AuthoringDocumentCandidate> m_Candidates = new(StringComparer.Ordinal);

    public AuthoringDocumentService(AuthoringWorkspaceDirectory workspace, IAuthoringDocumentDomain domain)
    { m_Workspace = workspace; m_Domain = domain; m_Commit.Saved += result => Saved?.Invoke(result); }

    public AuthoringWorkspaceDirectory Workspace => m_Workspace;
    public event Action<AuthoringDocumentCommitResult>? Saved;

    public AuthoringDocumentState ReadExternal(string rootPath)
    {
        string revision = "";
        try
        {
            AuthoringRootFiles source = m_Workspace.Read(rootPath);
            revision = source.Revision;
            var documents = new AuthoringDocumentSet(source);
            Receive(documents);
            return new(rootPath, source.Revision, documents, Array.Empty<AuthoringSourceProblem>());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            FormatException or Newtonsoft.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            return new(rootPath, revision, null, new[] { new AuthoringSourceProblem(rootPath,
                "authoring_input_invalid", error.Message) });
        }
    }

    public AuthoringDocumentCandidate Preview(string requestId, string rootPath, string baselineRevision,
        IReadOnlyList<AuthoringDocumentText> completeFiles)
    {
        AuthoringRootFiles baseline = Baseline(rootPath, baselineRevision);
        return Remember(AuthoringDocumentCandidate.Create(requestId, baseline, completeFiles, m_Domain));
    }

    public AuthoringDocumentCandidate PreviewEdits(string requestId, string rootPath, string baselineRevision,
        IReadOnlyList<AuthoringGraphEditBatch> graphBatches, IReadOnlyList<AuthoringTimelineEditBatch> timelineBatches)
    {
        AuthoringRootFiles baseline = Baseline(rootPath, baselineRevision);
        var documents = new AuthoringDocumentSet(baseline);
        string directory = Path.GetDirectoryName(baseline.Files[0].Path)!;
        var content = AuthoringDocumentCandidate.Texts(baseline).ToDictionary(file => file.Path, file => file.Text,
            StringComparer.OrdinalIgnoreCase);
        var graphs = new Dictionary<GraphAuthoringElementId, AuthoringGraphDocument>();
        foreach (AuthoringGraphEditBatch batch in graphBatches)
        {
            AuthoringGraphDocument graph = graphs.TryGetValue(batch.DocumentId, out var current) ? current :
                documents.Require<AuthoringGraphDocument>(batch.DocumentId);
            graphs[batch.DocumentId] = AuthoringGraphMutation.Apply(graph, batch.Edits).Document;
        }
        foreach (var graph in graphs)
        {
            string path = Path.GetRelativePath(directory, documents.Sources[graph.Key].Path).Replace('\\', '/');
            content[path] = AuthoringDocumentJson.WriteGraph(graph.Value);
        }
        var timelines = new Dictionary<GraphAuthoringElementId, AuthoringTimelineDocument>();
        foreach (AuthoringTimelineEditBatch batch in timelineBatches)
        {
            AuthoringTimelineDocument timeline = timelines.TryGetValue(batch.DocumentId, out var current) ? current :
                documents.Require<AuthoringTimelineDocument>(batch.DocumentId);
            timelines[batch.DocumentId] = AuthoringTimelineMutation.Apply(timeline, batch.Edits);
        }
        foreach (var timeline in timelines)
        {
            string path = Path.GetRelativePath(directory, documents.Sources[timeline.Key].Path).Replace('\\', '/');
            content[path] = AuthoringDocumentJson.WriteTimeline(timeline.Value);
        }
        return Remember(AuthoringDocumentCandidate.Create(requestId, baseline,
            content.Select(file => new AuthoringDocumentText(file.Key, file.Value)).ToArray(), m_Domain));
    }

    public AuthoringDocumentCandidate Candidate(string id) => m_Candidates[id];
    public AuthoringDocumentCommitResult? Query(string requestId) => m_Commit.Query(requestId);

    public AuthoringDocumentCandidate PreviewInverse(string requestId, string rootPath, string baselineRevision,
        string appliedCandidateId)
    {
        AuthoringDocumentCandidate applied = m_Candidates[appliedCandidateId];
        AuthoringDocumentCommitResult? receipt = m_Commit.Query(applied.RequestId);
        if (receipt?.Status != AuthoringCommitStatus.Saved)
            throw new InvalidOperationException("撤销和重做必须引用实际已保存的共同修改。");
        AuthoringRootFiles current = Baseline(rootPath, baselineRevision);
        if (!current.Root.Id.Equals(applied.Baseline.Root.Id))
            throw new InvalidOperationException("反向修改不能改变实际保存根。");
        return Remember(AuthoringDocumentCandidate.Create(requestId, current,
            AuthoringDocumentInverse.Create(applied.Baseline, applied.Documents.Source,
                applied.Differences.Select(file => file.DocumentId).Distinct().ToArray(), current), m_Domain));
    }

    public async Task<AuthoringDocumentCommitResult> Commit(string candidateId, CancellationToken cancellation = default)
    {
        AuthoringDocumentCommitResult result = await m_Commit.Commit(m_Candidates[candidateId], cancellation);
        return result;
    }

    AuthoringRootFiles Baseline(string rootPath, string revision)
    {
        AuthoringRootFiles source = m_Workspace.Read(rootPath);
        if (source.Revision != revision)
            throw new AuthoringBaselineConflictException(source.Root.Id.Value, revision, source.Revision);
        return source;
    }

    AuthoringDocumentCandidate Remember(AuthoringDocumentCandidate candidate)
    { m_Candidates.TryAdd(candidate.Id, candidate); return candidate; }

    void Receive(AuthoringDocumentSet documents)
    {
        if (!documents.Root.DomainId.Equals(m_Domain.DomainId) || documents.Root.DefinitionVersion != m_Domain.DefinitionVersion)
            throw new InvalidOperationException("作者根的领域或默认定义版本与当前正式合同不一致。");
        documents.ReceiveReferences();
        m_Domain.Receive(documents);
    }
}
}
