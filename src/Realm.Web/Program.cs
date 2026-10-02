using Realm.Demo;
using Realm.Infrastructure.Hosting;
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
builder.Logging.AddRealmConsole();   // one line per entry, UTC (03 section 9.1); the category levels come from appsettings.json and the log_level option
builder.Configuration.AddRealmOptionsFile();   // the add-on options on the 02 section 3.4 paths, just below the environment (03 section 2.1)
var runtime = RuntimeOptions.Detect(builder.Configuration, builder.Environment);
builder.Services.AddRealmApp(runtime, builder.Configuration);

var app = builder.Build();
app.UseRealmPipeline();
app.MapStaticAssets();
app.MapRealmEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
return 0;
