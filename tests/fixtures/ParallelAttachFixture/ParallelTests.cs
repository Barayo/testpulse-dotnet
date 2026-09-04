using System;
using System.Threading;
using TestPulse;
using Xunit;

namespace ParallelAttachFixture;

// Two different test classes -- xUnit's default parallelization unit is
// the test collection, and each class is its own collection by default,
// so these run genuinely concurrently with each other (unlike two
// [Fact]s in the SAME class, which run sequentially by default). The
// shared barrier forces real time-overlap, so this actually exercises
// concurrent Attach calls rather than merely asserting a correct
// end-state that could pass even if the calls never overlapped.
public static class Shared
{
    public static readonly Barrier Barrier = new(participantCount: 2);
}

public class ParallelTestsA
{
    [TestPulseCase("PARALLEL-A")]
    [Fact]
    public void AttachesUnderItsOwnCaseKey()
    {
        Shared.Barrier.SignalAndWait(TimeSpan.FromSeconds(10));
        TestPulseAttachments.Attach("PARALLEL-A", new byte[] { 0xA }, "a.png", "image/png");
    }
}

public class ParallelTestsB
{
    [TestPulseCase("PARALLEL-B")]
    [Fact]
    public void AttachesUnderItsOwnCaseKey()
    {
        Shared.Barrier.SignalAndWait(TimeSpan.FromSeconds(10));
        TestPulseAttachments.Attach("PARALLEL-B", new byte[] { 0xB }, "b.png", "image/png");
    }
}
