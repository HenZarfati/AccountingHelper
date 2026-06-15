using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AccountingHelper.Core.Services
{
    public class ExchangeRateService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        // Returns the Bank of Israel official representative rate (שער יציג) for USD/ILS — today's rate
        public async Task<decimal> GetUsdToIlsRateAsync()
        {
            var json = await _httpClient.GetStringAsync("https://boi.org.il/PublicApi/GetExchangeRates?key=USD");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                      .GetProperty("exchangeRates")[0]
                      .GetProperty("currentExchangeRate")
                      .GetDecimal();
        }

        // Returns BOI USD/ILS rate for a specific date.
        // If no rate published for that date (weekend/holiday), advances one day at a time until found.
        public async Task<decimal> GetUsdRateForDateAsync(DateTime startDate)
        {
            DateTime date = startDate;
            for (int i = 0; i < 10; i++)
            {
                string iso = date.ToString("yyyy-MM-dd");
                string url = $"https://boi.org.il/PublicApi/GetExchangeRates?key=USD&startDate={iso}&endDate={iso}";
                try
                {
                    var json = await _httpClient.GetStringAsync(url);
                    using var doc = JsonDocument.Parse(json);
                    var rates = doc.RootElement.GetProperty("exchangeRates");
                    if (rates.GetArrayLength() > 0)
                    {
                        decimal rate = rates[0].GetProperty("currentExchangeRate").GetDecimal();
                        if (rate > 0) return rate;
                    }
                }
                catch { }
                date = date.AddDays(1);
            }
            throw new InvalidOperationException($"Could not retrieve USD rate from Bank of Israel starting from {startDate:yyyy-MM-dd}.");
        }
    }
}
