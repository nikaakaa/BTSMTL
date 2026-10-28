using BTSMTL.Authoring.Workspace;

namespace BTSMTL.WebWorkbench;

public static class AuthoringDocumentApi
{
    public static void MapAuthoringDocumentApi(this WebApplication app, AuthoringDocumentService documents,
        Func<string> currentDefinitionsRevision)
    {
        RouteGroupBuilder routes = app.MapGroup("/api/authoring/documents");
        routes.MapGet("/state", (string path) =>
        {
            AuthoringDocumentState state = documents.ReadExternal(path);
            return new { state.RootPath, state.Revision, state.Adoptable, state.Problems };
        });
        routes.MapPost("/edits", (DocumentEditPreviewRequest request) => Receive(request, currentDefinitionsRevision,
            () => documents.PreviewEdits(request.RequestId.ToString("N"), request.RootPath,
                request.RootRevision, request.GraphBatches.Select(batch => new AuthoringGraphEditBatch(new(batch.DocumentId),
                    batch.Edits.Select(AuthoringGraphEditTransport.Read).ToArray())).ToArray(),
                request.TimelineBatches.Select(batch => new AuthoringTimelineEditBatch(new(batch.DocumentId),
                    batch.Edits.Select(AuthoringTimelineEditTransport.Read).ToArray())).ToArray())));
        routes.MapPost("/candidates", (DocumentCandidateRequest request) => Receive(request, currentDefinitionsRevision,
            () => documents.Preview(request.RequestId.ToString("N"), request.RootPath,
                request.RootRevision, request.Files.Select(file => new AuthoringDocumentText(file.Path, file.Text)).ToArray())));
        routes.MapPost("/inverse", (DocumentInverseRequest request) => Receive(request, currentDefinitionsRevision,
            () => documents.PreviewInverse(request.RequestId.ToString("N"), request.RootPath,
                request.RootRevision, request.AppliedCandidateId)));
        routes.MapGet("/candidates/{id}", (string id) => Candidate(documents.Candidate(id)));
        routes.MapPost("/candidates/{id}/commit", (string id, CancellationToken cancellation) => documents.Commit(id, cancellation));
        routes.MapGet("/requests/{id:guid}", (Guid id) =>
            documents.Query(id.ToString("N")) is { } result ? Results.Ok(result) : Results.NotFound());
    }

    static IResult Receive(IDocumentReceiveRequest request, Func<string> currentDefinitionsRevision,
        Func<AuthoringDocumentCandidate> receive)
    {
        string revision = currentDefinitionsRevision();
        if (revision != request.DefinitionsRevision)
            return Results.Conflict(new { request.RequestId, Stage = "definitions-conflict",
                ExpectedRevision = request.DefinitionsRevision, ActualRevision = revision });
        try { return Results.Ok(Candidate(receive())); }
        catch (AuthoringBaselineConflictException conflict)
        {
            return Results.Conflict(new { request.RequestId, Stage = "baseline-conflict", conflict.RootId,
                conflict.ExpectedRevision, conflict.CurrentRevision });
        }
        catch (Exception error) when (error is IOException or FormatException or Newtonsoft.Json.JsonException or
            ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return Results.UnprocessableEntity(new { request.RequestId, Stage = "candidate-receive", Message = error.Message });
        }
    }

    static object Candidate(AuthoringDocumentCandidate candidate) => new
    {
        CandidateId = candidate.Id, candidate.RequestId, Stage = "domain-received",
        RootId = candidate.Baseline.Root.Id.Value, BaselineRevision = candidate.Baseline.Revision,
        candidate.WriteSet, candidate.Differences,
        Files = AuthoringDocumentCandidate.Texts(candidate.Documents.Source)
    };
}
