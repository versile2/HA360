using Realm.Demo;
using Realm.Web;
using Realm.Web.Hosting;

if (args is ["healthcheck"]) return await HealthProbe.RunAsync();

var builder = WebApplication.CreateBuilder(args);
var runtime = RuntimeOptions.Detect(builder.Configuration, builder.Environment);
builder.Services.AddRealmWeb(runtime);
builder.Services.AddRealmDemo();   // until S13b wires the Live side, the Demo services are registered in every mode (R3-027)

var app = builder.Build();
app.UseRealmPipeline();
app.MapStaticAssets();
app.MapRealmEndpoints();
app.Run();
return 0;
