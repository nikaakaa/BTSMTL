using System.Text.Json.Serialization;
using BTSMTL.WebWorkbench;
using BTSMTL.Authoring.Workspace;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

string workspacePath = builder.Configuration["Workspace"]
    ?? throw new InvalidOperationException("请用 --Workspace 指定项目正式作者文档目录。");
app.MapWorkspaceApi(new AuthoringWorkspaceDirectory(workspacePath));

app.Run();
