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
            return new { state.RootPath, state.Revision, state.Adoptable, state.Stage, state.Problems,
                DefinitionsRevision = currentDefinitionsRevision() };
        });
        routes.MapPost("/edits", (DocumentEditPreviewRequest request) => Receive(request,
            () => documents.PreviewEdits(request.RequestId.ToString("N"), request.RootPath,
                request.RootRevision, request.DefinitionsRevision,
                request.GraphBatches.Select(batch => new AuthoringGraphEditBatch(new(batch.DocumentId),
                    batch.Edits.Select(AuthoringGraphEditTransport.Read).ToArray())).ToArray(),
                request.TimelineBatches.Select(batch => new AuthoringTimelineEditBatch(new(batch.DocumentId),
                    batch.Edits.Select(AuthoringTimelineEditTransport.Read).ToArray())).ToArray(), request.BaseCandidateId)));
        routes.MapPost("/candidates", (DocumentCandidateRequest request) => Receive(request,
            () => documents.Preview(request.RequestId.ToString("N"), request.RootPath,
                request.RootRevision, request.DefinitionsRevision,
                request.Files.Select(file => new AuthoringDocumentText(file.Path, file.Text)).ToArray(), request.BaseCandidateId)));
        routes.MapPost("/inverse", (DocumentInverseRequest request) => Receive(request,
            () => documents.PreviewInverse(request.RequestId.ToString("N"), request.RootPath,
                request.RootRevision, request.DefinitionsRevision, request.AppliedCandidateId, request.BaseCandidateId)));
        routes.MapGet("/candidates/{id}", (string id) => Candidate(documents.Candidate(id)));
        routes.MapPost("/candidates/{id}/commit", async (string id, CancellationToken cancellation) =>
        {
            try { return Results.Ok(await documents.Commit(id, cancellation)); }
            catch (InvalidOperationException error)
            { return Results.UnprocessableEntity(new { Stage = "candidate-commit", Message = error.Message }); }
        });
        routes.MapGet("/requests/{id:guid}", (Guid id) =>
            documents.Query(id.ToString("N")) is { } result ? Results.Ok(result) : Results.NotFound());
    }

    static IResult Receive(IDocumentReceiveRequest request, Func<AuthoringDocumentCandidate> receive)
    {
        try
        {
            if (request.RequestId == Guid.Empty) throw new ArgumentException("作者修改需要非空 requestId。");
            return Results.Ok(Candidate(receive()));
        }
        catch (AuthoringDefinitionsConflictException conflict)
        {
            return Results.Conflict(new { request.RequestId, Stage = "definitions-conflict",
                conflict.ExpectedRevision, ActualRevision = conflict.CurrentRevision });
        }
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
        CandidateId = candidate.Id, candidate.RequestId,
        Stage = candidate.Stage == AuthoringDocumentAcceptanceStage.Domain ? "domain-received" : "structure-received",
        SaveReady = candidate.Stage == AuthoringDocumentAcceptanceStage.Domain,
        Acceptance = candidate.Stage.ToString(), candidate.DefinitionsRevision,
        RootId = candidate.Baseline.Root.Id.Value, BaselineRevision = candidate.Baseline.Revision,
        InputRevision = candidate.Input.Revision,
        ReadSet = candidate.Baseline.Files.Concat(candidate.Dependencies).Select(file => new { file.Path, file.Revision }),
        candidate.WriteSet, candidate.Differences,
        Files = AuthoringDocumentCandidate.Texts(candidate.Documents.Source)
    };
}
