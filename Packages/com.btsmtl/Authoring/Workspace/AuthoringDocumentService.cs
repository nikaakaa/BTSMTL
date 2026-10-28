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
    AuthoringDocumentSet? Documents, AuthoringDocumentAcceptanceStage? Stage,
    IReadOnlyList<AuthoringSourceProblem> Problems)
{
    public bool Adoptable => Stage == AuthoringDocumentAcceptanceStage.Domain && Documents != null && Problems.Count == 0;
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
            AuthoringDocumentAcceptance acceptance = m_Domain.Receive(documents, null);
            acceptance.Documents.ReceiveReferences();
            return new(rootPath, source.Revision, acceptance.Documents, acceptance.Stage, Array.Empty<AuthoringSourceProblem>());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            FormatException or Newtonsoft.Json.JsonException or ArgumentException or InvalidOperationException)
        {
            return new(rootPath, revision, null, null, new[] { new AuthoringSourceProblem(rootPath,
                "authoring_input_invalid", error.Message) });
        }
    }

    public AuthoringDocumentCandidate Preview(string requestId, string rootPath, string baselineRevision,
        string definitionsRevision, IReadOnlyList<AuthoringDocumentText> completeFiles, string? baseCandidateId)
    {
        AuthoringRootFiles baseline = Baseline(rootPath, baselineRevision);
        AuthoringRootFiles input = Input(baseline, definitionsRevision, baseCandidateId);
        return Remember(AuthoringDocumentCandidate.Create(requestId, baseline, input, completeFiles, m_Domain,
            definitionsRevision));
    }

    public AuthoringDocumentCandidate PreviewEdits(string requestId, string rootPath, string baselineRevision,
        string definitionsRevision, IReadOnlyList<AuthoringGraphEditBatch> graphBatches,
        IReadOnlyList<AuthoringTimelineEditBatch> timelineBatches, string? baseCandidateId)
    {
        AuthoringRootFiles baseline = Baseline(rootPath, baselineRevision);
        AuthoringRootFiles input = Input(baseline, definitionsRevision, baseCandidateId);
        var documents = new AuthoringDocumentSet(input);
        string directory = Path.GetDirectoryName(baseline.Files[0].Path)!;
        var content = AuthoringDocumentCandidate.Texts(input).ToDictionary(file => file.Path, file => file.Text,
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
        return Remember(AuthoringDocumentCandidate.Create(requestId, baseline, input,
            content.Select(file => new AuthoringDocumentText(file.Key, file.Value)).ToArray(), m_Domain,
            definitionsRevision));
    }

    public AuthoringDocumentCandidate Candidate(string id) => m_Candidates[id];
    public AuthoringDocumentCommitResult? Query(string requestId) => m_Commit.Query(requestId);

    public AuthoringDocumentCandidate PreviewInverse(string requestId, string rootPath, string baselineRevision,
        string definitionsRevision, string appliedCandidateId, string? baseCandidateId)
    {
        AuthoringDocumentCandidate applied = m_Candidates[appliedCandidateId];
        AuthoringDocumentCommitResult? receipt = m_Commit.Query(applied.RequestId);
        if (baseCandidateId == null &&
            (receipt?.Status != AuthoringCommitStatus.Saved || receipt.CandidateId != applied.Id))
            throw new InvalidOperationException("撤销和重做必须引用实际已保存的共同修改。");
        AuthoringRootFiles current = Baseline(rootPath, baselineRevision);
        AuthoringRootFiles input = Input(current, definitionsRevision, baseCandidateId);
        if (!current.Root.Id.Equals(applied.Baseline.Root.Id))
            throw new InvalidOperationException("反向修改不能改变实际保存根。");
        return Remember(AuthoringDocumentCandidate.Create(requestId, current, input,
            AuthoringDocumentInverse.Create(applied.Input, applied.Documents.Source, input), m_Domain,
            definitionsRevision));
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

    AuthoringRootFiles Input(AuthoringRootFiles baseline, string definitionsRevision, string? candidateId)
    {
        if (candidateId == null) return baseline;
        AuthoringDocumentCandidate candidate = m_Candidates[candidateId];
        if (!string.Equals(candidate.Baseline.Files[0].Path, baseline.Files[0].Path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("继续编辑的候选必须属于同一物理作者根。");
        if (candidate.Baseline.Revision != baseline.Revision)
            throw new AuthoringBaselineConflictException(baseline.Root.Id.Value, candidate.Baseline.Revision, baseline.Revision);
        if (candidate.DefinitionsRevision != definitionsRevision)
            throw new AuthoringDefinitionsConflictException(candidate.DefinitionsRevision, definitionsRevision);
        return candidate.Documents.Source;
    }

}
}
