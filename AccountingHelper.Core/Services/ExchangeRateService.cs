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

        // Returns the BOI שער יציג (representative rate) for USD/ILS on or after startDate.
        // Uses the BOI SDMX historical-rates API (edge.boi.org.il) in CSV form.
        //
        // Two important quirks this handles:
        //  1. The SDMX "c[DIMENSION]=value" filter syntax contains square brackets, which
        //     .NET's HttpClient sends literally — the BOI server rejects those with 404.
        //     It is also extremely slow (~21s). So we request the dataflow UNFILTERED
        //     (no brackets, returns instantly) and filter to the representative USD rate here.
        //  2. The unfiltered response contains several USD series (actual, implied, volatility);
        //     the representative rate is the row with DATA_TYPE=OF00 and COUNTER_CURRENCY=ILS.
        public async Task<decimal> GetUsdRateForDateAsync(DateTime startDate)
        {
            string start = startDate.ToString("yyyy-MM-dd");
            string end   = startDate.AddDays(10).ToString("yyyy-MM-dd");

            string url = "https://edge.boi.org.il/FusionEdgeServer/sdmx/v2/data/dataflow/BOI.STATISTICS/EXR/1.0/" +
                         $"?startperiod={start}&endperiod={end}&format=csv";

            string csv = await HttpRetryHelper.GetStringAsync(_httpClient, url);

            var lines = csv.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
                throw new InvalidOperationException($"BOI returned no exchange rate data starting {start}.");

            // Resolve columns by header name (order is stable but we don't hard-code positions)
            var headers = lines[0].Split(',');
            int baseCurIdx    = Array.IndexOf(headers, "BASE_CURRENCY");
            int counterCurIdx = Array.IndexOf(headers, "COUNTER_CURRENCY");
            int dataTypeIdx   = Array.IndexOf(headers, "DATA_TYPE");
            int timePeriodIdx = Array.IndexOf(headers, "TIME_PERIOD");
            int obsValueIdx   = Array.IndexOf(headers, "OBS_VALUE");

            if (baseCurIdx < 0 || counterCurIdx < 0 || dataTypeIdx < 0 || timePeriodIdx < 0 || obsValueIdx < 0)
                throw new InvalidOperationException("Unexpected BOI SDMX CSV format.");

            int maxIdx = Math.Max(Math.Max(baseCurIdx, counterCurIdx),
                                  Math.Max(dataTypeIdx, Math.Max(timePeriodIdx, obsValueIdx)));

            DateTime? bestDate = null;
            decimal   bestRate = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                var fields = lines[i].Split(',');
                if (fields.Length <= maxIdx) continue;

                // Keep only the USD/ILS representative rate (שער יציג)
                if (!string.Equals(fields[baseCurIdx].Trim(),    "USD",  StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(fields[counterCurIdx].Trim(), "ILS",  StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(fields[dataTypeIdx].Trim(),   "OF00", StringComparison.OrdinalIgnoreCase)) continue;

                if (!DateTime.TryParse(fields[timePeriodIdx].Trim(), out DateTime lineDate)) continue;
                if (lineDate < startDate) continue;

                if (decimal.TryParse(fields[obsValueIdx].Trim(), NumberStyles.Any,
                                     CultureInfo.InvariantCulture, out decimal rate) && rate > 0)
                {
                    // Take the earliest published day on or after the requested date
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
