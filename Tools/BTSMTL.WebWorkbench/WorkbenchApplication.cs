using System.Text.Json.Serialization;

namespace BTSMTL.WebWorkbench;

public static class WorkbenchApplication
{
    public static WebApplication Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.IncludeFields = true;
            options.SerializerOptions.AllowOutOfOrderMetadataProperties = true;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        var app = builder.Build();
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            Exception exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()!.Error;
            context.Response.StatusCode = exception switch
            {
                FileNotFoundException or DirectoryNotFoundException or KeyNotFoundException => 404,
                ArgumentException or InvalidDataException or FormatException or OverflowException or System.Text.Json.JsonException => 400,
                InvalidOperationException => 409,
                _ => 500
            };
            await context.Response.WriteAsJsonAsync(new { Error = exception.Message });
        }));
        app.UseDefaultFiles();
        // 页面入口每次向 Host 核对，发布新前端后刷新即用新版；带哈希的 assets 仍按原样缓存。
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = file =>
            {
                if (file.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    file.Context.Response.Headers.CacheControl = "no-cache";
            }
        });
        app.MapAnimationResourceApi(builder.Configuration["AnimationLibrary"]);
        return app;
    }
}
