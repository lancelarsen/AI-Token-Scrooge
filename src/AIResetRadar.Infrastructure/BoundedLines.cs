using System.Runtime.CompilerServices;
using System.Text;

namespace AIResetRadar.Infrastructure;

internal static class BoundedLines
{
    public static async IAsyncEnumerable<string> ReadAsync(TextReader reader, int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var buffer = new char[4096];
        var line = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
        {
            for (var index = 0; index < count; index++)
            {
                if (buffer[index] == '\n')
                {
                    if (line.Length > 0 && line[^1] == '\r') line.Length--;
                    yield return line.ToString();
                    line.Clear();
                }
                else
                {
                    if (line.Length >= limit) throw new InvalidDataException("Provider response exceeded the monitor's limit.");
                    line.Append(buffer[index]);
                }
            }
        }
        if (line.Length > 0) yield return line.ToString();
    }
}
