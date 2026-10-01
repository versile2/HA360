using System.Net;
using Xunit;

namespace Realm.Web.Tests;

public class ErrorPlainTests
{
    [Fact]
    public async Task UnhandledException_ReturnsA500WithTheGenericTextOnly()
    {
        await using var host = await RealmTestHost.StartAsync();
        using var client = host.CreateClient();

        using var response = await client.GetAsync("boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Something went wrong in the Realm.", body);
        Assert.DoesNotContain("secret", body);
    }
}
