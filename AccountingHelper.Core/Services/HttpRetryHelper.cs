using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace AccountingHelper.Core.Services
{
    // Wraps HttpClient GET calls with retry-on-transient-failure and, on persistent
    // failure, throws an exception that includes the exact URL and HTTP status code
    // so errors surfaced in the UI are actionable instead of a bare "404 (Not Found)".
    // Every attempt is also written to a debug log for diagnosis.
    internal static class HttpRetryHelper
    {
        // Log path is fixed and easy to find so network failures can be diagnosed.
        public static readonly string LogPath =
            Path.Combine(Path.GetTempPath(), "AccountingHelper-net.log");

        public static async Task<string> GetStringAsync(HttpClient client, string url, int maxAttempts = 3)
        {
            string lastError = "";

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        Log($"OK {(int)response.StatusCode} (attempt {attempt}) {url}");
                        return await response.Content.ReadAsStringAsync();
                    }

                    lastError = $"{(int)response.StatusCode} ({response.ReasonPhrase})";
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    lastError = ex.Message;
                }

                Log($"FAIL [{lastError}] (attempt {attempt}/{maxAttempts}) {url}");

                // Back off before retrying (200ms, 600ms, ...), but not after the final attempt
                if (attempt < maxAttempts)
                    await Task.Delay(200 * attempt * attempt);
            }

            throw new InvalidOperationException(
                $"בקשת רשת נכשלה ({lastError}).\nכתובת: {url}");
        }

        private static void Log(string line)
        {
            try
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
            }
            catch { /* logging must never break the actual work */ }
        }
    }
}
