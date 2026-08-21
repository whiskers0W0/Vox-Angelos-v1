using System.Text.RegularExpressions;

namespace VoxAngelos.IntegrationTests.TestSupport;

/// <summary>
/// Reads the running app's own Development-mode console log (redirected to a file when
/// it was launched) to recover values the app deliberately never returns over HTTP —
/// e.g. an email-change confirmation link, which only ever goes out by email. Legitimate
/// here because EmailSender's dev-diagnostic dump (see Services/EmailSender.cs) always
/// logs the full email body before checking Testing:SuppressExternalNotifications.
/// </summary>
public static class AppLogReader
{
    public const string LogPath =
        @"C:\Users\user\AppData\Local\Temp\claude\C--Users-user-source-repos-vox-angelos-v1\8f07b317-e087-4404-9489-4e7cddd5688a\scratchpad\app_run.log";

    public static long CurrentLength() => new FileInfo(LogPath).Length;

    /// <summary>Reads only the log content appended since <paramref name="fromPosition"/>.</summary>
    public static async Task<string> ReadSinceAsync(long fromPosition)
    {
        await using var stream = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(fromPosition, SeekOrigin.Begin);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <summary>Polls the log for a confirmation URL addressed to a given recipient, appearing after fromPosition.</summary>
    public static async Task<string> WaitForConfirmationLinkAsync(long fromPosition, string toEmailContains, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            // The console logger indents every continuation line of a multi-line log
            // message (matching the "warn: "-style category prefix width), so each line
            // below is allowed arbitrary leading whitespace rather than matched exactly.
            var text = await ReadSinceAsync(fromPosition);
            var blockMatches = Regex.Matches(
                text,
                @"==================== DEV EMAIL ====================\s*To:\s*(?<to>\S+)\s*Subject:\s*(?<subject>[^\r\n]+)\s*[-]+\s*(?<body>.*?)\s*=====================================================",
                RegexOptions.Singleline);

            foreach (Match block in blockMatches)
            {
                if (!block.Groups["to"].Value.Contains(toEmailContains, StringComparison.OrdinalIgnoreCase))
                    continue;

                var urlMatch = Regex.Match(block.Groups["body"].Value, @"Confirm new email address:\s*(https?://\S+)");
                if (urlMatch.Success)
                    return urlMatch.Groups[1].Value;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"No email-change confirmation link addressed to '{toEmailContains}' appeared in the app log within {timeout}.");
    }
}
