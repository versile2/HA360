namespace Realm.Web.Tests;

/// <summary>What the test-only <c>echo</c> endpoint saw of a request after the Realm pipeline ran.</summary>
internal sealed record RequestEcho(string Scheme, string Host, string PathBase, string Path);
