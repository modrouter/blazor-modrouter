namespace ModRouter.Test;

[TestFixture]
public sealed class SmokeTests
{
    [Test]
    public void Test_project_runs()
    {
        var sum = 1 + 1;
        Assert.That(sum, Is.EqualTo(2));
    }
}
