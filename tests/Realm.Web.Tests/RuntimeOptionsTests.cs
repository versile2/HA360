using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Realm.Web.Hosting;
using Xunit;

namespace Realm.Web.Tests;

public class RuntimeOptionsTests
{
    private const string DemoModeTrue = """{ "demo_mode": true }""";
    private const string DemoModeFalse = """{ "demo_mode": false }""";

    [Theory]
    [InlineData(null, null, null, RealmMode.Demo)]                // no token: Demo (CI, a laptop)
    [InlineData(null, "tok", null, RealmMode.Live)]               // a token and nothing else: Live
    [InlineData(null, "tok", DemoModeFalse, RealmMode.Live)]
    [InlineData(null, "tok", DemoModeTrue, RealmMode.Demo)]       // demo_mode beats the token
    [InlineData("demo", "tok", null, RealmMode.Demo)]             // REALM_DATA_SOURCE beats the token
    [InlineData("DEMO", "tok", null, RealmMode.Demo)]
    [InlineData("ha", null, null, RealmMode.Live)]                // forced Live even without a token
    [InlineData("ha", "tok", DemoModeTrue, RealmMode.Live)]       // REALM_DATA_SOURCE beats demo_mode
    [InlineData("auto", "tok", null, RealmMode.Live)]             // an unknown value is ignored
    [InlineData(null, "tok", "{ not json", RealmMode.Live)]       // a malformed options file never throws
    [InlineData(null, "tok", "[]", RealmMode.Live)]
    [InlineData(null, "tok", """{ "demo_mode": "true" }""", RealmMode.Live)]
    public void Detect_AppliesThePrecedence(string? dataSource, string? token, string? optionsJson, RealmMode expected)
    {
        var optionsPath = NewOptionsPath();
        try
        {
            if (optionsJson is not null)
            {
                File.WriteAllText(optionsPath, optionsJson);
            }

            var configuration = Configure(new Dictionary<string, string?>
            {
                ["REALM_DATA_SOURCE"] = dataSource,
                ["SUPERVISOR_TOKEN"] = token,
                ["Realm:OptionsPath"] = optionsPath,
            });

            Assert.Equal(expected, RuntimeOptions.Detect(configuration, new TestEnvironment("Production")).Mode);
        }
        finally
        {
            File.Delete(optionsPath);
        }
    }

    [Fact]
    public void Detect_ReadsTheOptionsPathFromTheEnvironmentFirst()
    {
        var optionsPath = NewOptionsPath();
        try
        {
            File.WriteAllText(optionsPath, DemoModeTrue);
            var configuration = Configure(new Dictionary<string, string?>
            {
                ["SUPERVISOR_TOKEN"] = "tok",
                ["REALM_OPTIONS"] = optionsPath,
                ["Realm:OptionsPath"] = NewOptionsPath(),
            });

            Assert.Equal(RealmMode.Demo, RuntimeOptions.Detect(configuration, new TestEnvironment("Production")).Mode);
        }
        finally
        {
            File.Delete(optionsPath);
        }
    }

    [Fact]
    public void Detect_AcceptsTheDevelopmentTokenKey()
    {
        var configuration = Configure(new Dictionary<string, string?>
        {
            ["Realm:Ha:Token"] = "dev-token",
            ["Realm:OptionsPath"] = NewOptionsPath(),
        });

        Assert.Equal(RealmMode.Live, RuntimeOptions.Detect(configuration, new TestEnvironment("Production")).Mode);
    }

    [Theory]
    [InlineData(null, "Production", true)]       // Demo
    [InlineData("tok", "Production", false)]     // Live
    [InlineData("tok", "Development", true)]
    public void Detect_EnablesDetailedErrorsForDemoAndDevelopment(string? token, string environmentName, bool expected)
    {
        var configuration = Configure(new Dictionary<string, string?>
        {
            ["SUPERVISOR_TOKEN"] = token,
            ["Realm:OptionsPath"] = NewOptionsPath(),
        });

        Assert.Equal(expected, RuntimeOptions.Detect(configuration, new TestEnvironment(environmentName)).DetailedErrors);
    }

    // Never exists, so a developer's own /data/options.json cannot change a result.
    private static string NewOptionsPath() => Path.Combine(Path.GetTempPath(), $"realm-options-{Guid.NewGuid():N}.json");

    private static IConfiguration Configure(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class TestEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Realm.Web.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
