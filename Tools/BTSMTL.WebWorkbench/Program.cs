using BTSMTL.WebWorkbench;

var app = WorkbenchApplication.Create(args);
app.MapPreviewApi(null);
app.MapPreviewImageApi(null);
app.Run();
