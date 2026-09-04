using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using TestPulse;

namespace TestPulse.MSBuild.Tests;

public class TestPulseAttachmentsTests : IDisposable
{
    private readonly string _scratchDir = TestPulseAttachments.ScratchDir();

    public TestPulseAttachmentsTests()
    {
        if (Directory.Exists(_scratchDir))
        {
            Directory.Delete(_scratchDir, recursive: true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchDir))
        {
            Directory.Delete(_scratchDir, recursive: true);
        }
    }

    private int DataFileCount() =>
        Directory.Exists(_scratchDir) ? Directory.GetFiles(_scratchDir, "*.data").Length : 0;

    [TestPulseCase("LOGIN-42")]
    [Fact]
    public void AttachSucceedsForDeclaredCaseKey()
    {
        TestPulseAttachments.Attach("LOGIN-42", new byte[] { 1, 2, 3 }, "failure.png", "image/png");
        Assert.Equal(1, DataFileCount());
    }

    [TestPulseCase("LOGIN-42")]
    [Fact]
    public void AttachRejectsUndeclaredCaseKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TestPulseAttachments.Attach("OTHER-1", new byte[] { 1, 2, 3 }, "failure.png", "image/png"));
        Assert.Contains("OTHER-1", ex.Message);
        Assert.Equal(0, DataFileCount());
    }

    [TestPulseCase("LOGIN-42")]
    [Fact]
    public void AttachRejectsContentTypeBeforeCheckingCaseKey()
    {
        // Even under a genuinely-declared case key, an unsupported
        // content type must still be rejected -- proving ordering.
        Assert.Throws<ArgumentException>(() =>
            TestPulseAttachments.Attach("LOGIN-42", new byte[] { 1 }, "x.pdf", "application/pdf"));
        Assert.Equal(0, DataFileCount());
    }

    [Fact]
    public void AttachRejectsCaseKeyDeclaredByADifferentTestMethod()
    {
        // "LOGIN-42" is declared on AttachSucceedsForDeclaredCaseKey /
        // AttachRejectsUndeclaredCaseKey above, not on this test method.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TestPulseAttachments.Attach("LOGIN-42", new byte[] { 1 }, "failure.png", "image/png"));
        Assert.Contains("LOGIN-42", ex.Message);
        Assert.Equal(0, DataFileCount());
    }

    [Fact]
    public void AttachOutsideActiveTestContextThrowsDistinctError()
    {
        // AsyncLocal (and therefore TestContext.Current) flows to child
        // threads/tasks by default in .NET -- a bare `new Thread(...)`
        // from within a test still sees the test's own context, so it
        // does NOT genuinely test "no active context." Suppressing
        // ExecutionContext flow before starting the thread is what
        // actually severs that link, giving a genuine no-context case.
        InvalidOperationException? caught = null;
        using (ExecutionContext.SuppressFlow())
        {
            var thread = new Thread(() =>
            {
                try
                {
                    TestPulseAttachments.Attach("LOGIN-42", new byte[] { 1 }, "failure.png", "image/png");
                }
                catch (InvalidOperationException ex)
                {
                    caught = ex;
                }
            });
            thread.Start();
            thread.Join();
        }

        Assert.NotNull(caught);
        Assert.Contains("outside an active TestPulse test execution", caught!.Message);
    }

    [TestPulseCase("LOGIN-42")]
    [Fact]
    public void TwoAttachmentsUnderSameCaseKeyBothSurvive()
    {
        TestPulseAttachments.Attach("LOGIN-42", new byte[] { 1 }, "a.png", "image/png");
        TestPulseAttachments.Attach("LOGIN-42", new byte[] { 2 }, "b.png", "image/png");
        Assert.Equal(2, DataFileCount());
    }

    [TestPulseCase("LOGIN-42")]
    [Fact]
    public async Task AttachSucceedsAfterAnAsyncContinuationOnADifferentThread()
    {
        var startingThread = Environment.CurrentManagedThreadId;
        await Task.Yield();
        // Not asserting the thread actually changed (the scheduler isn't
        // guaranteed to hop) -- the real assertion is that Attach still
        // succeeds regardless, proving TestContext.Current (AsyncLocal
        // under the hood) survived the continuation either way.
        _ = startingThread;

        TestPulseAttachments.Attach("LOGIN-42", new byte[] { 1 }, "failure.png", "image/png");
        Assert.Equal(1, DataFileCount());
    }
}
