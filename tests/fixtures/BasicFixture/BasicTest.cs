using TestPulse;
using Xunit;

namespace BasicFixture;

public class BasicTest
{
    [TestPulseCase("LOGIN-42")]
    [Fact]
    public void LoginSucceeds()
    {
    }
}
