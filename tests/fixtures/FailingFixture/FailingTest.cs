using TestPulse;
using Xunit;

namespace FailingFixture;

public class FailingTest
{
    [TestPulseCase("LOGIN-42")]
    [Fact]
    public void ThisTestGenuinelyFails()
    {
        Assert.Fail("a real regression");
    }
}
