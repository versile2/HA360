using Realm.Demo;
using Realm.Web;
using Realm.Web.Components;
using Realm.Web.Hosting;

if (args is ["healthcheck"]) return await HealthProbe.RunAsync();

// CLI mode: write the fictional cast as JSON for the TypeScript tests (03 section 7.2) and exit before the host is built.
if (args is ["export-demo-cast", ..])
{
    if (args is not [_, var castFile])
    {
        await Console.Error.WriteLineAsync("Usage: export-demo-cast <file>");
        return 64;
    }

    DemoCastExporter.WriteTo(castFile);
    return 0;
}

var builder = WebApplication.CreateBuilder(args);
var runtime = RuntimeOptions.Detect(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(runtime);   // RealmShell reads the mode to decide whether the Demo-only URL parameters apply (03 section 2.1)
builder.Services.AddRealmWeb(runtime);
builder.Services.AddRealmDemo();   // until S13b wires the Live side, the Demo services are registered in every mode (R3-027)

var app = builder.Build();
app.UseRealmPipeline();
app.MapStaticAssets();
app.MapRealmEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
return 0;
