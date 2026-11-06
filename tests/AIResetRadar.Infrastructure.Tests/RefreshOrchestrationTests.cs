using AIResetRadar.App;
using AIResetRadar.App.Services;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

[Collection("App orchestration")]
public sealed class RefreshOrchestrationTests
{
    static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FasterAccountPublishesAndCanRefreshAgainWhileFirstReadIsPending(bool manual)
    {
        using var files = new HistoryFixture();
        var slow = files.Account;
        var fast = slow with { Id = "second", Subject = "second-user", CredentialId = "second-credential" };
        var slowResult = ResultGate();
        var firstFastResult = ResultGate();
        var nextFastResult = ResultGate();
        var slowCalls = 0;
        var fastCalls = 0;
        var monitor = new TestCredentialMonitor
        {
            OnRefresh = (account, _) =>
            {
                if (account.Id == slow.Id)
                {
                    Interlocked.Increment(ref slowCalls);
                    return slowResult.Task;
                }
                return Interlocked.Increment(ref fastCalls) == 1 ? firstFastResult.Task : nextFastResult.Task;
            }
        };
        var page = new MainPage(files.Store, monitor, slow, null, false);
        page.AddAccountForTest(fast);
        var first = files.Snapshot(files.Now) with { AccountId = fast.Id, Subject = fast.Subject };
        var next = first with { ObservedAt = first.ObservedAt.AddMinutes(1) };
        var initialPass = page.RefreshForTestAsync(force: false);
        var overlappingPass = Task.CompletedTask;
        try
        {
            var firstCompletion = page.ReadCompletionForTest(fast.Id);
            firstFastResult.SetResult(first);
            await firstCompletion.WaitAsync(Deadline);
            Assert.False(initialPass.IsCompleted);
            Assert.Equal(fast.Id, Assert.Single((await files.Store.ReadAsync<QuotaSnapshot[]>("snapshots"))!).AccountId);
            Assert.Single(await files.Store.HistoryAsync(fast.Id));
            Assert.True(page.RenderCountForTest > 0);
            Assert.True(page.TrayUpdateCountForTest > 0);

            // A second timer tick can pick up a newly due account. Manual refresh
            // can also refresh it immediately, despite its normal future due time.
            if (!manual) page.MarkDueForTest(fast.Id);
            overlappingPass = page.RefreshForTestAsync(force: manual);
            var nextCompletion = page.ReadCompletionForTest(fast.Id);
            nextFastResult.SetResult(next);
            await nextCompletion.WaitAsync(Deadline);
            var saved = Assert.Single((await files.Store.ReadAsync<QuotaSnapshot[]>("snapshots"))!);
            Assert.Equal(next.ObservedAt, saved.ObservedAt);
            Assert.False(initialPass.IsCompleted);
        }
        finally
        {
            firstFastResult.TrySetResult(first);
            nextFastResult.TrySetResult(next);
            slowResult.SetResult(files.Snapshot(files.Now));
            await Task.WhenAll(initialPass, overlappingPass).WaitAsync(Deadline);
        }
        Assert.Equal(1, slowCalls);
        Assert.Equal(2, fastCalls);
        Assert.Equal(2, (await files.Store.ReadAsync<QuotaSnapshot[]>("snapshots"))!.Length);
    }

    [Fact]
    public async Task ForcedRefreshHonorsProviderRetryDeadlineWhileOtherAccountSucceeds()
    {
        using var files = new HistoryFixture();
        var throttled = files.Account;
        var healthy = throttled with { Id = "healthy", Subject = "healthy-user", CredentialId = "healthy-credential" };
        var throttledCalls = 0;
        var healthyCalls = 0;
        var snapshot = files.Snapshot(files.Now) with { AccountId = healthy.Id, Subject = healthy.Subject };
        var monitor = new TestCredentialMonitor
        {
            OnRefresh = (account, _) =>
            {
                if (account.Id == throttled.Id)
                {
                    Interlocked.Increment(ref throttledCalls);
                    throw new ProviderAccessException("Fixture retry deadline", SourceHealth.Offline, files.Now.AddHours(1));
                }
                Interlocked.Increment(ref healthyCalls);
                return Task.FromResult(snapshot);
            }
        };
        var page = new MainPage(files.Store, monitor, throttled, null, false);
        await page.RefreshForTestAsync(force: false);
        page.AddAccountForTest(healthy);
        await page.RefreshForTestAsync(force: true);
        Assert.Equal(1, throttledCalls);
        Assert.Equal(1, healthyCalls);
        var saved = (await files.Store.ReadAsync<QuotaSnapshot[]>("snapshots"))!;
        Assert.Equal(SourceHealth.Offline, saved.Single(s => s.AccountId == throttled.Id).Health);
        Assert.Equal(SourceHealth.Healthy, saved.Single(s => s.AccountId == healthy.Id).Health);
        Assert.Empty(await files.Store.HistoryAsync(throttled.Id));
        Assert.Single(await files.Store.HistoryAsync(healthy.Id));
    }

    [Fact]
    public async Task FailedHistoryCommitRetainsPreviousReadingWhileAnotherAccountPublishes()
    {
        using var files = new HistoryFixture();
        var failed = files.Account;
        var healthy = failed with { Id = "healthy", Subject = "healthy-user", CredentialId = "healthy-credential" };
        var previous = files.Snapshot(files.Now.AddMinutes(-1));
        await files.Store.WriteMonitoringStateAsync([failed, healthy], [previous], historySnapshot: previous);
        await files.ExecuteAsync("""
            CREATE TRIGGER reject_fixture_read BEFORE INSERT ON history
            WHEN NEW.account_id = 'a' BEGIN SELECT RAISE(ABORT, 'fixture history write failed'); END;
            """);

        var failedResult = ResultGate();
        var healthyResult = ResultGate();
        var monitor = new TestCredentialMonitor
        {
            OnRefresh = (account, _) => account.Id == failed.Id ? failedResult.Task : healthyResult.Task
        };
        var page = new MainPage(files.Store, monitor, failed, null, false);
        page.AddAccountForTest(healthy);
        page.SetSnapshotForTest(previous);
        var next = previous with { ObservedAt = files.Now, Windows = [previous.Windows.Single() with { Used = 99 }] };
        var polling = page.RefreshForTestAsync(force: false);
        try
        {
            var failedCompletion = page.ReadCompletionForTest(failed.Id);
            failedResult.SetResult(next);
            await failedCompletion.WaitAsync(Deadline);
            var visible = page.SnapshotForTest(failed.Id);
            Assert.Equal(SourceHealth.Stale, visible.Health);
            Assert.Equal(previous.ObservedAt, visible.ObservedAt);
            Assert.Equal(previous.Windows.Single().Used, visible.Windows.Single().Used);
        }
        finally
        {
            failedResult.TrySetResult(next);
            healthyResult.SetResult(next with { AccountId = healthy.Id, Subject = healthy.Subject });
            await polling.WaitAsync(Deadline);
        }
        var saved = (await files.Store.ReadAsync<QuotaSnapshot[]>("snapshots"))!;
        var retained = saved.Single(s => s.AccountId == failed.Id);
        Assert.Equal(previous.ObservedAt, retained.ObservedAt);
        Assert.Equal(previous.Windows.Single().Used, retained.Windows.Single().Used);
        Assert.Equal(previous.ObservedAt, Assert.Single(await files.Store.HistoryAsync(failed.Id)).ObservedAt);
        Assert.Equal(next.ObservedAt, saved.Single(s => s.AccountId == healthy.Id).ObservedAt);
        Assert.Equal(next.ObservedAt, Assert.Single(await files.Store.HistoryAsync(healthy.Id)).ObservedAt);
    }

    [Fact]
    public async Task LateSuccessFromDisabledAccountIsDiscarded()
    {
        using var files = new HistoryFixture();
        var account = files.Account;
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = ResultGate();
        var monitor = new TestCredentialMonitor
        {
            OnRefresh = (_, token) => { started.SetResult(token); return result.Task; }
        };
        var page = new MainPage(files.Store, monitor, account, null, false);
        var pending = page.RefreshForTestAsync(force: false);
        try
        {
            var token = await started.Task.WaitAsync(Deadline);
            await page.EditForTestAsync(account with { Enabled = false });
            Assert.True(token.IsCancellationRequested);
        }
        finally
        {
            result.SetResult(files.Snapshot(files.Now));
            await pending.WaitAsync(Deadline);
        }
        Assert.Empty((await files.Store.ReadAsync<QuotaSnapshot[]>("snapshots"))!);
        Assert.Empty(await files.Store.HistoryAsync(account.Id));
    }

    [Fact]
    public async Task ShutdownCancelsEveryReadAndWaitsForEachToFinish()
    {
        using var files = new HistoryFixture();
        var first = files.Account;
        var second = first with { Id = "second", Subject = "second-user", CredentialId = "second-credential" };
        var firstStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstResult = ResultGate();
        var secondResult = ResultGate();
        var monitor = new TestCredentialMonitor
        {
            OnRefresh = (account, token) =>
            {
                if (account.Id == first.Id) { firstStarted.SetResult(token); return firstResult.Task; }
                secondStarted.SetResult(token); return secondResult.Task;
            }
        };
        var page = new MainPage(files.Store, monitor, first, null, false);
        page.AddAccountForTest(second);
        var polling = page.RefreshForTestAsync(force: false);
        var draining = Task.CompletedTask;
        try
        {
            var tokens = await Task.WhenAll(firstStarted.Task, secondStarted.Task).WaitAsync(Deadline);
            var firstFinished = page.ReadCompletionForTest(first.Id);
            draining = page.StopRefreshesForTestAsync();
            Assert.All(tokens, token => Assert.True(token.IsCancellationRequested));
            firstResult.SetCanceled();
            await firstFinished.WaitAsync(Deadline);
            Assert.False(draining.IsCompleted);
        }
        finally
        {
            firstResult.TrySetCanceled();
            secondResult.SetCanceled();
            await Task.WhenAll(polling, draining).WaitAsync(Deadline);
        }
        Assert.Empty(await files.Store.HistoryAsync(first.Id));
        Assert.Empty(await files.Store.HistoryAsync(second.Id));
    }

    static TaskCompletionSource<QuotaSnapshot> ResultGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
