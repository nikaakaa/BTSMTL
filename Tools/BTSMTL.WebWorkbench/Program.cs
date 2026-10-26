using System.Text.Json.Serialization;
using BTSMTL.WebWorkbench;
using BTSMTL.Authoring.Workspace;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(builder.Environment.ContentRootPath, "node_modules", "three")),
    RequestPath = "/vendor/three"
});

string workspacePath = builder.Configuration["Workspace"]
    ?? throw new InvalidOperationException("请用 --Workspace 指定项目正式作者文档目录。");
app.MapWorkspaceApi(new AuthoringWorkspaceDirectory(workspacePath));
app.MapAnimationResourceApi(builder.Configuration["AnimationLibrary"]);

app.Run();
