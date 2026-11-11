using BTSMTL.Authoring.Graph;
using BTSMTL.Authoring.Workspace;

namespace BTSMTL.WebWorkbench;

public static class AuthoringResourceApi
{
    internal static void Map(WebApplication app, string workspace, IAuthoringResourceAdapter adapter)
    {
        AuthoringCommits commits = AuthoringCommits.For(workspace);
        app.MapPost("/api/authoring/scopes/read", (GraphAuthoringScopeQuery query) => commits.Read(() =>
            Task.FromResult(Execute(commits, () => adapter.ReadScope(workspace, query)))));
        app.MapPost("/api/authoring/resources/objects", (GraphAuthoringResourceObjectQuery query) => commits.Read(() =>
            Task.FromResult(Objects(adapter, workspace, query))));
    }

    static IResult Objects(IAuthoringResourceAdapter adapter, string workspace, GraphAuthoringResourceObjectQuery query)
    {
        try { return Results.Json(adapter.ResourceObjects(workspace, query)); }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or Newtonsoft.Json.JsonException)
        {
            return Failure("resource-objects", error.Message, 400, "not-requested");
        }
        catch (InvalidOperationException error)
        {
            return Failure("resource-objects-catalog-conflict", error.Message, 409, "not-requested");
        }
    }

    public static IResult Execute<T>(AuthoringCommits commits, Func<T> operation)
    {
        try { return Results.Json(operation()); }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or Newtonsoft.Json.JsonException)
        {
            return Failure("authoring-adapter", exception.Message, 400, "not-completed");
        }
        catch (InvalidOperationException exception)
        {
            return Failure("authoring-adapter-conflict", exception.Message, 409, "not-completed");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("authoring-adapter-io", exception.Message, 500,
                File.Exists(Path.Combine(commits.Root, AuthoringCommits.JournalName)) ? "recovery-required" : "not-completed");
        }
    }

    static IResult Failure(string code, string message, int status, string authorState) => Results.Json(new
    {
        success = false, code, message, authorState, runtimeState = "not-applied"
    }, statusCode: status);
}
