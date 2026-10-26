using AIResetRadar.Infrastructure;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public class BoundedLinesTests
{
    [Fact]
    public async Task PreservesFramesAcrossReadBoundariesAndFinalUnterminatedLine()
    {
        var first = new string('a', 4095);
        var input = first + "\r\n\n" + new string('b', 4100) + "\nlast";
        var lines = new List<string>();
        await foreach (var line in BoundedLines.ReadAsync(new StringReader(input), 5000)) lines.Add(line);
        Assert.Equal(new[] { first, "", new string('b', 4100), "last" }, lines);
    }

    [Fact]
    public async Task RejectsOversizedUnterminatedInputBeforeReadingTheWholeStream()
    {
        using var input = new CountingReader(new string('x', 100_000));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in BoundedLines.ReadAsync(input, 5000)) { }
        });
        Assert.InRange(input.CharactersRead, 5001, 5000 + 4096);
    }

    [Fact]
    public async Task AcceptsExactLimitAndHonorsCancellation()
    {
        var lines = new List<string>();
        await foreach (var line in BoundedLines.ReadAsync(new StringReader("12345\n"), 5)) lines.Add(line);
        Assert.Equal(new[] { "12345" }, lines);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in BoundedLines.ReadAsync(new StringReader("123"), 5, canceled.Token)) { }
        });
    }

    sealed class CountingReader(string value) : StringReader(value)
    {
        public int CharactersRead { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            CharactersRead += count;
            return count;
        }
    }
}
