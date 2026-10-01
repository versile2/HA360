namespace Realm.Domain.Tests;

public class DrillFailingTests
{
    [Fact]
    public void Deliberately_fails() => Assert.Equal(1, 2);
}
