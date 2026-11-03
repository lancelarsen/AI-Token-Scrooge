using System.Diagnostics;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public sealed class CliBoundaryTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "ScroogeCliTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("claude.exe")]
    [InlineData("codex.cmd")]
    public void ExecutablePathNeverUsesShellOrPathResolution(string executable)
    {
        Assert.Throws<FileNotFoundException>(() => CliExecutablePath.Validate(executable));
    }

    [Fact] public async Task CodexDiscoverySkipsRedirectedInstallSubdirectories()
    {
        if (!OperatingSystem.IsWindows()) return;
        var installs = Path.Combine(folder, "installs"); var outside = Path.Combine(folder, "outside");
        Directory.CreateDirectory(installs); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "codex.exe"), "synthetic-not-executed");
        var link = Path.Combine(installs, "redirected");
        var start = Start("New-Item -ItemType Junction -Path $env:SCROOGE_TEST_ALIAS -Target $env:SCROOGE_TEST_TARGET | Out-Null");
        start.Environment["SCROOGE_TEST_ALIAS"] = link; start.Environment["SCROOGE_TEST_TARGET"] = outside;
        try
        {
            var result = await BoundedCliProcess.RunAsync(start, TimeSpan.FromSeconds(10), default);
            Assert.Equal(0, result.ExitCode);
            Assert.Null(CodexLocator.FindIn(installs));
            Assert.Throws<IOException>(() => CliExecutablePath.Validate(Path.Combine(link, "codex.exe")));
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link, false); }
    }

    [Fact] public void EnvironmentFilteringRemovesCredentialsAndStartupOverridesButKeepsWindowsAndProxySettings()
    {
        var removed = new[] { "OPENAI_API_KEY", "CODEX_HOME", "ANTHROPIC_API_KEY", "claude_code_oauth_token", "GITHUB_TOKEN", "GH_TOKEN", "AZURE_OPENAI_API_KEY", "GEMINI_API_KEY", "GOOGLE_APPLICATION_CREDENTIALS", "AWS_SECRET_ACCESS_KEY", "AWS_PROFILE", "NODE_OPTIONS", "NODE_PATH", "BUN_OPTIONS" };
        var env = removed.ToDictionary(key => key, _ => (string?)"synthetic-private");
        env["SystemRoot"] = "windows"; env["PATH"] = "system-path"; env["HTTPS_PROXY"] = "proxy-config";
        CliEnvironment.Sanitize(env);
        Assert.Equal(3, env.Count);
        Assert.Equal("windows", env["SystemRoot"]); Assert.Equal("system-path", env["PATH"]); Assert.Equal("proxy-config", env["HTTPS_PROXY"]);
        Assert.DoesNotContain("synthetic-private", env.Values);
    }

    [Fact] public void ProviderLaunchesUseAbsoluteIsolatedProfilesAndArgumentList()
    {
        var profile = Path.GetFullPath(Path.Combine(folder, "profile with spaces"));
        var codex = CodexConnection.StartInfo("codex.exe", profile);
        var claude = ClaudeClient.StartInfo("claude.exe", profile);
        Assert.False(codex.UseShellExecute); Assert.False(claude.UseShellExecute);
        Assert.Equal(profile, codex.WorkingDirectory); Assert.Equal(profile, claude.WorkingDirectory);
        Assert.Equal(profile, codex.Environment["CODEX_HOME"]); Assert.Equal(profile, claude.Environment["CLAUDE_CONFIG_DIR"]);
        Assert.DoesNotContain("CLAUDE_CONFIG_DIR", codex.Environment.Keys); Assert.DoesNotContain("CODEX_HOME", claude.Environment.Keys);
        Assert.Equal(new[] { "app-server", "--stdio", "-c", "cli_auth_credentials_store=\"keyring\"", "-c", "mcp_servers={}", "-c", "analytics.enabled=false" }, codex.ArgumentList);
        Assert.Empty(codex.Arguments); Assert.Empty(claude.Arguments);
    }

    ProcessStartInfo Start(string body)
    {
        Directory.CreateDirectory(folder);
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder };
        start.Environment["SCROOGE_TEST_PID"] = Path.Combine(folder, "pid");
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; [IO.File]::WriteAllText($env:SCROOGE_TEST_PID,[string]$PID); " + body }) start.ArgumentList.Add(arg);
        return start;
    }

    void AssertProcessEnded()
    {
        var id = int.Parse(File.ReadAllText(Path.Combine(folder, "pid")));
        try { using var process = Process.GetProcessById(id); Assert.True(process.HasExited); }
        catch (ArgumentException) { }
    }

    [Fact] public async Task StdoutIsReturnedButStderrIsDiscarded()
    {
        if (!OperatingSystem.IsWindows()) return;
        var result = await BoundedCliProcess.RunAsync(Start("[Console]::Out.Write('safe-output'); [Console]::Error.Write('synthetic-private'); exit 7"), TimeSpan.FromSeconds(10), default);
        Assert.Equal(7, result.ExitCode); Assert.Equal("safe-output", result.Output);
        Assert.DoesNotContain("synthetic-private", result.ToString()); AssertProcessEnded();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedOutputStopsProcessWithoutReturningPrivateText(bool stderr)
    {
        if (!OperatingSystem.IsWindows()) return;
        var stream = stderr ? "Error" : "Out";
        var start = Start($"[Console]::{stream}.Write(('synthetic-private' * 10000)); [Threading.Thread]::Sleep(30000)");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => BoundedCliProcess.RunAsync(start, TimeSpan.FromSeconds(10), default, outputLimit: 4096));
        Assert.DoesNotContain("synthetic-private", error.ToString()); AssertProcessEnded();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeadlineOrCallerCancellationStopsTheChild(bool callerCancellation)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var cancellation = new CancellationTokenSource();
        if (callerCancellation) cancellation.CancelAfter(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BoundedCliProcess.RunAsync(Start("[Threading.Thread]::Sleep(30000)"),
            callerCancellation ? TimeSpan.FromSeconds(20) : TimeSpan.FromSeconds(5), cancellation.Token));
        AssertProcessEnded();
    }

    [Fact] public async Task AlreadyCanceledRequestDoesNotStartAProcess()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BoundedCliProcess.RunAsync(Start("exit 0"), TimeSpan.FromSeconds(10), cancellation.Token));
        Assert.False(File.Exists(Path.Combine(folder, "pid")));
    }

    [Fact] public async Task CallerCancellationStopsAnOwnedDescendantWhileItsParentIsRunning()
    {
        if (!OperatingSystem.IsWindows()) return;
        var childPidPath = Path.Combine(folder, "child-pid");
        var childCommand = "[IO.File]::WriteAllText($env:SCROOGE_TEST_CHILD_PID,[string]$PID); [Threading.Thread]::Sleep(30000)";
        var start = Start("""
            $childStart = New-Object System.Diagnostics.ProcessStartInfo;
            $childStart.FileName = Join-Path $PSHOME 'powershell.exe';
            $childStart.Arguments = '-NoProfile -NonInteractive -EncodedCommand ' + $env:SCROOGE_TEST_CHILD_COMMAND;
            $childStart.UseShellExecute = $false;
            $childStart.CreateNoWindow = $true;
            $child = [Diagnostics.Process]::Start($childStart);
            $child.WaitForExit();
            """);
        start.Environment["SCROOGE_TEST_CHILD_PID"] = childPidPath;
        start.Environment["SCROOGE_TEST_CHILD_COMMAND"] = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(childCommand));
        using var cancellation = new CancellationTokenSource();
        var running = BoundedCliProcess.RunAsync(start, TimeSpan.FromSeconds(20), cancellation.Token);
        Process? child = null;
        try
        {
            var readyBy = Stopwatch.StartNew();
            int childId;
            while (!File.Exists(childPidPath) || !int.TryParse(File.ReadAllText(childPidPath), out childId))
            {
                Assert.False(running.IsCompleted, "Synthetic parent exited before its child was ready.");
                Assert.True(readyBy.Elapsed < TimeSpan.FromSeconds(10), "Synthetic child did not become ready.");
                await Task.Delay(25);
            }
            child = Process.GetProcessById(childId);
            // Retain the exact process handle before cancellation, avoiding a later PID lookup.
            _ = child.Handle;
            Assert.False(child.HasExited);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(child.HasExited);
            AssertProcessEnded();
        }
        finally
        {
            cancellation.Cancel();
            try { await running; } catch { }
            if (child is not null)
            {
                if (!child.HasExited)
                {
                    child.Kill(entireProcessTree: true);
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                }
                child.Dispose();
            }
        }
    }

    public void Dispose()
    {
        // Exit checks above remain mandatory. Windows can briefly retain a test
        // directory handle while the terminated parent/child processes finish teardown.
        var elapsed = Stopwatch.StartNew();
        while (Directory.Exists(folder))
        {
            try { Directory.Delete(folder, true); }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(3)) { Thread.Sleep(50); }
        }
    }
}
