using System.Net;
using System.Net.Http.Headers;
using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

// The scripted handler of Realm.TestKit answers from a queue, records what it was asked and throws when nothing is scripted (03 section 8.1 rule 6).
public class ScriptedHttpHandlerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Responses_come_in_the_order_they_were_scripted_and_every_request_is_recorded()
    {
        var clock = new ManualTimeProvider(Start);
        var handler = new ScriptedHttpHandler(clock)
            .Respond(HttpStatusCode.BadGateway)
            .Respond(HttpStatusCode.OK, """{"message":"API running."}""")
            .RespondWith(request => new HttpResponseMessage(HttpStatusCode.Accepted) { ReasonPhrase = request.Method.Method });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://ha.invalid/api/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "fake-token");

        using var first = await client.GetAsync("config");
        clock.Advance(TimeSpan.FromMilliseconds(250));
        using var second = await client.PostAsync("template", new StringContent("""{"template":"{{ 1 }}"}"""));
        using var third = await client.DeleteAsync("states/x");

        Assert.Equal(HttpStatusCode.BadGateway, first.StatusCode);
        Assert.Equal("""{"message":"API running."}""", await second.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Accepted, third.StatusCode);
        Assert.Equal("DELETE", third.ReasonPhrase);
        Assert.Equal(
            [("GET", "http://ha.invalid/api/config", Start), ("POST", "http://ha.invalid/api/template", Start.AddMilliseconds(250)), ("DELETE", "http://ha.invalid/api/states/x", Start.AddMilliseconds(250))],
            handler.Requests.Select(request => (request.Method.Method, request.Uri?.ToString() ?? string.Empty, request.At)));
        Assert.Equal("Bearer fake-token", handler.Requests[0].Headers["Authorization"]);
        Assert.Null(handler.Requests[0].Body);
        Assert.Equal("""{"template":"{{ 1 }}"}""", handler.Requests[1].Body);
    }

    [Fact]
    public async Task A_request_with_nothing_scripted_throws_so_no_call_goes_unnoticed()
    {
        var handler = new ScriptedHttpHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://ha.invalid/api/") };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("states"));

        Assert.Contains("GET http://ha.invalid/api/states", error.Message);
        Assert.Single(handler.Requests); // it was still recorded
    }

    [Fact]
    public async Task A_scripted_failure_is_thrown_to_the_caller()
    {
        var handler = new ScriptedHttpHandler().Fail(new HttpRequestException("connection refused")).Respond(HttpStatusCode.OK);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://ha.invalid/api/") };

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("states"));
        using var retry = await client.GetAsync("states");

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(2, handler.Requests.Count);
    }
}
