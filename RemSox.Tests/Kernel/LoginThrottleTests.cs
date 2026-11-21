using System.Diagnostics;

using RemSox.Kernel.Accounts;

namespace RemSox.Tests.Kernel;

/// <summary>The limits on failed network logins, on a clock the tests move by hand.</summary>
[Collection(KernelState.Name)]
public sealed class LoginThrottleTests : IDisposable
{
    private long now = 1_000_000_000;

    public LoginThrottleTests()
    {
        LoginThrottle.Clock = () => now;
        LoginThrottle.Reset();
    }

    public void Dispose()
    {
        LoginThrottle.Clock = Stopwatch.GetTimestamp;
        LoginThrottle.Reset();
    }

    [Fact]
    public void FirstFailuresAreFree()
    {
        for (int i = 0; i < LoginThrottle.FreeFailures; i++)
        {
            Assert.Null(LoginThrottle.Refuse("bob"));
            LoginThrottle.Failed("bob");
        }
    }

    [Fact]
    public void NameWaitsAfterTheFreeFailures()
    {
        FailTimes("bob", LoginThrottle.FreeFailures);

        Assert.Equal("Too many failed logins; try again in 2 s.", LoginThrottle.Refuse("bob"));
        Advance(TimeSpan.FromSeconds(2));
        Assert.Null(LoginThrottle.Refuse("bob"));
    }

    [Fact]
    public void WaitDoublesWithEachFailure()
    {
        FailTimes("bob", LoginThrottle.FreeFailures + 2);

        Assert.Equal("Too many failed logins; try again in 8 s.", LoginThrottle.Refuse("bob"));
    }

    [Fact]
    public void WaitStopsGrowingAtTheMaximum()
    {
        // Spread out, so the overall budget never runs out.
        for (int i = 0; i < 30; i++)
        {
            LoginThrottle.Failed("bob");
            Advance(LoginThrottle.Refill);
        }

        Advance(-LoginThrottle.Refill);
        Assert.Equal($"Too many failed logins; try again in {LoginThrottle.MaxNameDelay.TotalSeconds} s.", LoginThrottle.Refuse("bob"));
    }

    [Fact]
    public void OtherNamesAreNotKeptWaiting()
    {
        FailTimes("bob", LoginThrottle.FreeFailures);

        Assert.Null(LoginThrottle.Refuse("carol"));
    }

    [Fact]
    public void NameIsComparedWithoutCaseOrSpaces()
    {
        FailTimes("Bob", LoginThrottle.FreeFailures);

        Assert.NotNull(LoginThrottle.Refuse(" bob "));
    }

    [Fact]
    public void RightPasswordForgetsTheFailures()
    {
        FailTimes("bob", LoginThrottle.FreeFailures);

        LoginThrottle.Succeeded("bob");

        Assert.Null(LoginThrottle.Refuse("bob"));
        LoginThrottle.Failed("bob");
        Assert.Null(LoginThrottle.Refuse("bob"));
    }

    [Fact]
    public void FailuresAreForgottenAfterAQuietWhile()
    {
        FailTimes("bob", LoginThrottle.FreeFailures);

        Advance(LoginThrottle.ForgetAfter + TimeSpan.FromSeconds(1));
        LoginThrottle.Failed("bob");

        // Counting starts again: one failure is free.
        Assert.Null(LoginThrottle.Refuse("bob"));
    }

    [Fact]
    public void FailuresAcrossNamesUseUpTheOverallBudget()
    {
        for (int i = 0; i < LoginThrottle.Burst; i++)
        {
            LoginThrottle.Failed("guess" + i);
        }

        Assert.Equal($"Too many failed logins; try again in {LoginThrottle.Refill.TotalSeconds} s.", LoginThrottle.Refuse("bob"));
        Advance(LoginThrottle.Refill);
        Assert.Null(LoginThrottle.Refuse("bob"));
    }

    [Fact]
    public void BudgetNeverGrowsBeyondTheBurst()
    {
        Advance(TimeSpan.FromHours(1));

        for (int i = 0; i < LoginThrottle.Burst; i++)
        {
            LoginThrottle.Failed("guess" + i);
        }

        Assert.NotNull(LoginThrottle.Refuse("bob"));
    }

    /// <summary>
    /// Fails <paramref name="times"/> times for <paramref name="name"/>, a refill apart, so the
    /// overall budget stays and only the name's own wait shows; the last failure is now.
    /// </summary>
    private void FailTimes(string name, int times)
    {
        for (int i = 0; i < times; i++)
        {
            if (i > 0)
            {
                Advance(LoginThrottle.Refill);
            }

            LoginThrottle.Failed(name);
        }
    }

    private void Advance(TimeSpan time)
    {
        now += (long)(time.TotalSeconds * Stopwatch.Frequency);
    }
}
