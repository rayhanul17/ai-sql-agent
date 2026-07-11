using System.Runtime.CompilerServices;

namespace SqlAgent.Infrastructure.Ai;

/// <summary>
/// Removes reasoning blocks (&lt;think&gt;...&lt;/think&gt;) from a streamed token
/// sequence so users never see a model's chain-of-thought. Works across token
/// boundaries (the tags may be split between chunks) by buffering only while a
/// tag might be forming.
/// </summary>
public static class ThinkFilter
{
    private const string Open = "<think>";
    private const string Close = "</think>";

    public static async IAsyncEnumerable<string> StripAsync(
        IAsyncEnumerable<string> source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var buffer = string.Empty;
        var inThink = false;

        await foreach (var chunk in source.WithCancellation(ct))
        {
            buffer += chunk;

            while (buffer.Length > 0)
            {
                if (inThink)
                {
                    var closeIdx = buffer.IndexOf(Close, StringComparison.OrdinalIgnoreCase);
                    if (closeIdx < 0)
                    {
                        // Keep a small tail in case a close tag straddles chunks.
                        buffer = Tail(buffer, Close.Length);
                        break;
                    }
                    buffer = buffer[(closeIdx + Close.Length)..];
                    inThink = false;
                }
                else
                {
                    var openIdx = buffer.IndexOf(Open, StringComparison.OrdinalIgnoreCase);
                    if (openIdx < 0)
                    {
                        // Emit everything except a possible partial "<think>" tail.
                        var safe = Tail(buffer, Open.Length);
                        var emit = buffer[..(buffer.Length - safe.Length)];
                        if (emit.Length > 0) yield return emit;
                        buffer = safe;
                        break;
                    }
                    if (openIdx > 0) yield return buffer[..openIdx];
                    buffer = buffer[(openIdx + Open.Length)..];
                    inThink = true;
                }
            }
        }

        // Flush any trailing text that isn't inside a think block.
        if (!inThink && buffer.Length > 0)
            yield return buffer;
    }

    // Returns up to (n-1) trailing chars that could be the start of a tag.
    private static string Tail(string s, int tagLen)
    {
        var keep = Math.Min(tagLen - 1, s.Length);
        return s[^keep..];
    }
}
