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
        var configuration = Configure("""{ "retention_fix_days": 45, "allow_demo_param": true }""");

        Assert.Equal("45", configuration["Retention:FixDays"]);
        Assert.Equal("true", configuration["Demo:AllowParam"]);
        Assert.Equal("false", configuration["Demo:Mode"]);
        Assert.Null(configuration[RealmLiveServiceCollectionExtensions.OptionsFileErrorKey]);
    }

    [Fact]
    public void TheFile_SitsJustBelowTheEnvironmentVariables_SoTheEnvironmentWins()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Realm:OptionsPath"] = WriteOptions("""{ "retention_fix_days": 45 }""") });
        configuration.AddEnvironmentVariables();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Retention:FixDays"] = "99" });   // stands for the command line, above the environment

        configuration.AddRealmOptionsFile();

        var sources = configuration.Sources;
        var environment = IndexOf<EnvironmentVariablesConfigurationSource>(sources);
        var file = Assert.IsType<MemoryConfigurationSource>(sources[environment - 1]);
        Assert.Equal("45", file.InitialData?.Single(pair => pair.Key == "Retention:FixDays").Value);
        Assert.Equal("99", configuration["Retention:FixDays"]);
    }

    [Fact]
    public void TheEnvironmentNamesAnotherFile_AndItBeatsTheConfiguredPath()
    {
        var configured = WriteOptions("""{ "retention_fix_days": 45 }""", "configured.json");
        var named = WriteOptions("""{ "retention_fix_days": 50 }""", "named.json");
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Realm:OptionsPath"] = configured, ["REALM_OPTIONS"] = named });

        configuration.AddRealmOptionsFile();

        Assert.Equal("50", configuration["Retention:FixDays"]);
    }

    [Fact]
    public void AValueOfTheWrongType_IsReportedByKeyName_AndNeverByValue()
    {
        var configuration = Configure("""{ "retention_fix_days": "a-value-that-must-not-be-echoed" }""");

        var error = configuration[RealmLiveServiceCollectionExtensions.OptionsFileErrorKey];

        Assert.NotNull(error);
        Assert.Contains("retention_fix_days", error, StringComparison.Ordinal);
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
