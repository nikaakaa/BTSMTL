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
        app.UseStaticFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
                Path.Combine(builder.Environment.ContentRootPath, "node_modules", "three")),
            RequestPath = "/vendor/three"
        });
        app.MapAnimationResourceApi(builder.Configuration["AnimationLibrary"]);
        return app;
    }
}
