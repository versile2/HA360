using Xunit;

namespace Realm.Domain.Tests;

public class SmokeTests
{
    [Fact]
    public void TestProjectRuns()
    {
        Assert.Equal("Realm.Domain.Tests", typeof(SmokeTests).Assembly.GetName().Name);
    }
}
