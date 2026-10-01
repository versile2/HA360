using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Realm.Infrastructure.Hosting;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// <c>AddRealmOptionsFile</c> puts the Supervisor's <c>options.json</c> on the configuration paths of 02 section 3.4, as a source just below the environment
/// variables (03 section 2.1). A file that is absent adds nothing; one that cannot be used adds a message that names the key and never the value.
/// </summary>
public sealed class OptionsFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "realm-options-" + Guid.NewGuid().ToString("N"));

    public OptionsFileTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void AMissingFile_AddsNothing()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Realm:OptionsPath"] = Path.Combine(_directory, "absent.json") });
        var before = configuration.Sources.Count;

        configuration.AddRealmOptionsFile();

        Assert.Equal(before, configuration.Sources.Count);
        Assert.Null(configuration[RealmLiveServiceCollectionExtensions.OptionsFileErrorKey]);
    }

    [Fact]
    public void AValidFile_IsMappedOntoTheConfigurationPaths_AndTheDefaultsFillWhatItLeavesOut()
    {
        var configuration = Configure("""{ "ui_stale_after_minutes": 45, "allow_demo_param": true }""");

        Assert.Equal("45", configuration["Ui:StaleAfterMinutes"]);
        Assert.Equal("true", configuration["Demo:AllowParam"]);
        Assert.Equal("24", configuration["Ui:OfflineAfterHours"]);
        Assert.Null(configuration[RealmLiveServiceCollectionExtensions.OptionsFileErrorKey]);
    }

    [Fact]
    public void TheFile_SitsJustBelowTheEnvironmentVariables_SoTheEnvironmentWins()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Realm:OptionsPath"] = WriteOptions("""{ "ui_stale_after_minutes": 45 }""") });
        configuration.AddEnvironmentVariables();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Ui:StaleAfterMinutes"] = "99" });   // stands for the command line, above the environment

        configuration.AddRealmOptionsFile();

        var sources = configuration.Sources;
        var environment = IndexOf<EnvironmentVariablesConfigurationSource>(sources);
        var file = Assert.IsType<MemoryConfigurationSource>(sources[environment - 1]);
        Assert.Equal("45", file.InitialData?.Single(pair => pair.Key == "Ui:StaleAfterMinutes").Value);
        Assert.Equal("99", configuration["Ui:StaleAfterMinutes"]);
    }

    [Fact]
    public void TheEnvironmentNamesAnotherFile_AndItBeatsTheConfiguredPath()
    {
        var configured = WriteOptions("""{ "ui_stale_after_minutes": 45 }""", "configured.json");
        var named = WriteOptions("""{ "ui_stale_after_minutes": 50 }""", "named.json");
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Realm:OptionsPath"] = configured, ["REALM_OPTIONS"] = named });

        configuration.AddRealmOptionsFile();

        Assert.Equal("50", configuration["Ui:StaleAfterMinutes"]);
    }

    [Fact]
    public void AValueOfTheWrongType_IsReportedByKeyName_AndNeverByValue()
    {
        var configuration = Configure("""{ "ui_stale_after_minutes": "a-value-that-must-not-be-echoed" }""");

        var error = configuration[RealmLiveServiceCollectionExtensions.OptionsFileErrorKey];

        Assert.NotNull(error);
        Assert.Contains("ui_stale_after_minutes", error, StringComparison.Ordinal);
        Assert.DoesNotContain("a-value-that-must-not-be-echoed", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    public void AFileThatIsNotJson_IsReportedWithAFixedSentence(string content)
    {
        var configuration = Configure(content);

        Assert.Equal("The options file could not be read as JSON", configuration[RealmLiveServiceCollectionExtensions.OptionsFileErrorKey]);
    }

    [Fact]
    public void AFileThatIsNotAnObject_IsReported()
    {
        var configuration = Configure("[1, 2]");

        Assert.Equal("The options file must hold one JSON object", configuration[RealmLiveServiceCollectionExtensions.OptionsFileErrorKey]);
    }

    private ConfigurationManager Configure(string optionsJson)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Realm:OptionsPath"] = WriteOptions(optionsJson) });
        configuration.AddRealmOptionsFile();
        return configuration;
    }

    private string WriteOptions(string json, string name = "options.json")
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, json);
        return path;
    }

    private static int IndexOf<TSource>(IList<IConfigurationSource> sources)
        where TSource : IConfigurationSource
    {
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is TSource)
            {
                return i;
            }
        }

        throw new InvalidOperationException("The source is not in the list");
    }
}
