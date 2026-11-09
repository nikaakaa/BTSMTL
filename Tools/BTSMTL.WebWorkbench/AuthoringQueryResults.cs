using System.Text;
using System.Text.Json;
using BTSMTL.Authoring.Workspace;
using Newtonsoft.Json.Linq;

namespace BTSMTL.WebWorkbench;

// 作者查询的 HTTP 回执：成功按规范紧凑文本返回，已知的作者错误按分类转成 4xx 并记录请求选择器。
public static class AuthoringQueryResults
{
    public static JsonElement[] Documents(AuthoringDocumentService service, IEnumerable<AuthoringDocumentSnapshot> documents) => documents
        .Select(document => JsonSerializer.Deserialize<JsonElement>(AuthoringJsonText.Text(service.Describe(document, true))))
        .ToArray();

    public static IResult Read(HttpContext context, object? selector, Func<JObject> query)
    {
        try
        {
            return Result(query(), 200);
        }
        catch (Exception exception) when (Handles(exception))
        {
            return Failure(context, selector, exception);
        }
    }

    public static async Task<IResult> ReadAsync(HttpContext context, object? selector, Func<Task<JObject>> query)
    {
        try
        {
            return Result(await query(), 200);
        }
        catch (Exception exception) when (Handles(exception))
        {
            return Failure(context, selector, exception);
        }
    }

    static bool Handles(Exception exception) => exception is AuthoringJsonFormatException or
        Newtonsoft.Json.JsonException or InvalidDataException or IOException or ArgumentException or InvalidOperationException;

    static IResult Failure(HttpContext context, object? selector, Exception exception)
    {
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(AuthoringQueryResults).FullName!)
            .LogError(exception,
                "作者查询失败 TraceIdentifier={TraceIdentifier} Method={Method} Path={Path} DocumentPath={DocumentPath} Selector={Selector}",
                context.TraceIdentifier, context.Request.Method, context.Request.Path,
                context.Request.Query["documentPath"].ToString(), JsonSerializer.Serialize(selector));
        if (exception is AuthoringJsonFormatException format)
            return Result(format.Result(), 422);
        (string code, int status) = exception switch
        {
            Newtonsoft.Json.JsonException => ("authoring-json-syntax", 422),
            InvalidDataException or IOException => ("source-asset", 422),
            ArgumentException => ("authoring-request", 400),
            _ => ("authoring-conflict", 409)
        };
        return Result(new JObject { ["success"] = false, ["code"] = code, ["message"] = exception.Message }, status);
    }

    static IResult Result(JObject result, int statusCode)
    {
        if (result.Property("availability") == null)
            result["availability"] = new JObject
            {
                ["available"] = statusCode == 200,
                ["reason"] = statusCode == 200 ? null : result["message"]
            };
        return Results.Content(AuthoringJsonText.Compact(result), "application/json", Encoding.UTF8, statusCode);
    }
}
