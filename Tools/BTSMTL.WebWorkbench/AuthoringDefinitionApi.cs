using System.Text.Json;

namespace BTSMTL.WebWorkbench;

public interface IAuthoringDefinitionQueries
{
    object Info();
    object Document();
    object List(int offset, int limit, string? role);
    object Require(string id);
    object ProjectPorts(AuthoringGraphPortRequest request);
    object ValidateStateMachine(AuthoringStateMachineValidationRequest request);
    object ValidateFlowGraph(AuthoringFlowGraphValidationRequest request);
}

public static class AuthoringDefinitionApi
{
    public static void MapAuthoringDefinitionApi(this WebApplication app, IAuthoringDefinitionQueries catalog)
    {
        RouteGroupBuilder routes = app.MapGroup("/api/authoring");
        routes.AddEndpointFilter(async (context, next) =>
        {
            try { return await next(context); }
            catch (Exception error) when (error is IOException or JsonException or
                Newtonsoft.Json.JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                return Results.UnprocessableEntity(new { success = false, stage = "authoring-validation",
                    requestId = context.HttpContext.TraceIdentifier, message = error.Message });
            }
        });
        routes.MapGet("/definitions", catalog.Info);
        routes.MapGet("/definitions/document", catalog.Document);
        routes.MapGet("/capabilities", (int offset, int limit, string? role) => catalog.List(offset, limit, role));
        routes.MapGet("/capabilities/{id}", (string id) => catalog.Require(id));
        routes.MapPost("/ports", catalog.ProjectPorts);
        routes.MapPost("/state-machines/validate", catalog.ValidateStateMachine);
        routes.MapPost("/graphs/validate", catalog.ValidateFlowGraph);
    }
}
