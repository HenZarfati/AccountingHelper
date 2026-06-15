using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AccountingHelper.Core.Services
{
    public class ExchangeRateService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        // Returns the Bank of Israel official representative rate (שער יציג) for USD/ILS — today's rate.
        public async Task<decimal> GetUsdToIlsRateAsync()
        {
            var json = await _httpClient.GetStringAsync("https://boi.org.il/PublicApi/GetExchangeRates?key=USD");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                      .GetProperty("exchangeRates")[0]
                      .GetProperty("currentExchangeRate")
                      .GetDecimal();
        }

        // Returns the BOI שער יציג for USD/ILS on or after startDate.
        // Uses the BOI SDMX historical-rates API (edge.boi.org.il) which supports date filtering.
        // The PublicApi endpoint always returns the current rate and ignores date parameters.
        public async Task<decimal> GetUsdRateForDateAsync(DateTime startDate)
        {
            string start = startDate.ToString("yyyy-MM-dd");
            string end   = startDate.AddDays(10).ToString("yyyy-MM-dd");

            // BOI SDMX v2 API — returns historical representative exchange rates in CSV
            string url = "https://edge.boi.org.il/FusionEdgeServer/sdmx/v2/data/dataflow/BOI.STATISTICS/EXR/1.0/" +
                         $"?c[DATA_TYPE]=OF00&c[BASE_CURRENCY]=USD&startperiod={start}&endperiod={end}&format=csv";

            string csv = await HttpRetryHelper.GetStringAsync(_httpClient, url);

            var lines = csv.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
                throw new InvalidOperationException($"BOI returned no exchange rate data for USD starting {start}.");

            // Header: SERIES_CODE,FREQ,BASE_CURRENCY,...,TIME_PERIOD,OBS_VALUE,RELEASE_STATUS
            var headers = lines[0].Split(',');
            int timePeriodIdx = Array.IndexOf(headers, "TIME_PERIOD");
            int obsValueIdx   = Array.IndexOf(headers, "OBS_VALUE");

            if (timePeriodIdx < 0 || obsValueIdx < 0)
                throw new InvalidOperationException("Unexpected BOI SDMX CSV format.");

            DateTime? bestDate = null;
            decimal   bestRate = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                var fields = lines[i].Split(',');
                if (fields.Length <= Math.Max(timePeriodIdx, obsValueIdx)) continue;

                if (!DateTime.TryParse(fields[timePeriodIdx].Trim(), out DateTime lineDate)) continue;
                if (lineDate < startDate) continue;

                if (decimal.TryParse(fields[obsValueIdx].Trim(), NumberStyles.Any,
                                     CultureInfo.InvariantCulture, out decimal rate) && rate > 0)
                {
                    if (bestDate == null || lineDate < bestDate)
                    {
                        bestDate = lineDate;
                        bestRate = rate;
                    }
                }
            }

            if (bestRate > 0) return bestRate;

            throw new InvalidOperationException(
                $"Could not retrieve USD/ILS rate from Bank of Israel starting from {startDate:yyyy-MM-dd}.");
        }
    }
}
