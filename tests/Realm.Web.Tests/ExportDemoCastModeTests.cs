using System.Reflection;
using System.Text.Json;
using Realm.Demo;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The <c>export-demo-cast &lt;file&gt;</c> mode of Program.cs (03 section 7.2): it writes the fictional cast and exits before the host is built.
/// The tests run the real entry point of the Realm.Web assembly in this process, so the argument handling in Program.cs is what is under test.
/// </summary>
public sealed class ExportDemoCastModeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"realm-export-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExportDemoCast_WritesTheCastAsJson_AndReturnsZero()
    {
        var path = Path.Combine(_folder, "demo-cast.json");   // the folder does not exist yet: the exporter creates it

        var exitCode = await RunProgramAsync("export-demo-cast", path);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(path));
        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("The King's Wagon", json);
        Assert.Contains(DemoCast.Wagon.Lore, json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task ExportDemoCast_WithoutExactlyOneFile_IsAUsageError_AndWritesNothing(int extraArguments)
    {
        var arguments = new[] { "export-demo-cast" }.Concat(Enumerable.Range(0, extraArguments).Select(i => Path.Combine(_folder, $"{i}.json"))).ToArray();

        var exitCode = await RunProgramAsync(arguments);

        Assert.Equal(64, exitCode);
        Assert.False(Directory.Exists(_folder));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // The entry point of a top-level-statements program that awaits is a generated int Main; its Task<int> twin is handled too.
    private static async Task<int> RunProgramAsync(params string[] arguments)
    {
        var entryPoint = typeof(RuntimeOptions).Assembly.EntryPoint
            ?? throw new InvalidOperationException("Realm.Web has no entry point.");

        try
        {
            return entryPoint.Invoke(null, [arguments]) switch
            {
                int exitCode => exitCode,
                Task<int> task => await task,
                var other => throw new InvalidOperationException($"Unexpected entry point result: {other}"),
            };
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
