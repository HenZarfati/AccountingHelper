using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace AccountingHelper.Core.Services
{
    // Wraps HttpClient GET calls with retry-on-transient-failure and, on persistent
    // failure, throws an exception that includes the exact URL and HTTP status code
    // so errors surfaced in the UI are actionable instead of a bare "404 (Not Found)".
    internal static class HttpRetryHelper
    {
        public static async Task<string> GetStringAsync(HttpClient client, string url, int maxAttempts = 3)
        {
            HttpStatusCode? lastStatus = null;
            string lastError = "";

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                        return await response.Content.ReadAsStringAsync();

                    lastStatus = response.StatusCode;
                    lastError  = $"{(int)response.StatusCode} ({response.ReasonPhrase})";
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                {
                    lastError = ex.Message;
                }

                // Back off before retrying (200ms, 600ms, ...), but not after the final attempt
                if (attempt < maxAttempts)
                    await Task.Delay(200 * attempt * attempt);
            }

            throw new InvalidOperationException(
                $"בקשת רשת נכשלה ({lastError}).\nכתובת: {url}");
        }
    }
}
