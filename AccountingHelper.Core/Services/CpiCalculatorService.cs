using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AccountingHelper.Core.Services
{
    public class CpiCalculatorService
    {
        private static readonly HttpClient _http = new HttpClient();
        private const string CpiIndexId = "120010";

        static CpiCalculatorService()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("AccountingHelper/1.0");
        }

        // Returns target date: 25th of previous month if today < 25, else 25th of current month.
        // If that date is Saturday, use Sunday instead.
        public static DateTime GetTargetDate()
        {
            var today = DateTime.Today;
            var target = today.Day >= 25
                ? new DateTime(today.Year, today.Month, 25)
                : new DateTime(today.Year, today.Month, 25).AddMonths(-1);
            if (target.DayOfWeek == DayOfWeek.Saturday)
                target = target.AddDays(1);
            return target;
        }

        // Calculates the indexed amount using CBS series-linked CPI.
        // CBS convention: use CPI of the month BEFORE each reference date.
        // When base and target are in different CBS base-year series, accumulates
        // linking factors (avg of transition year / 100) across each series boundary.
        public async Task<decimal> CalculateIndexedAmountAsync(decimal baseAmount, DateTime baseDate, DateTime targetDate)
        {
            var baseCpiMonth   = baseDate.AddMonths(-1);
            var targetCpiMonth = targetDate.AddMonths(-1);

            var (baseCpi,   baseSeries)   = await GetCpiWithSeriesAsync(baseCpiMonth);
            var (targetCpi, targetSeries) = await GetCpiWithSeriesAsync(targetCpiMonth);

            decimal ratio = targetCpi / baseCpi;

            if (baseSeries == targetSeries)
                return Math.Round(baseAmount * ratio, 2);

            decimal linkingFactor = await GetAccumulatedLinkingFactorAsync(baseSeries, targetSeries);
            return Math.Round(baseAmount * ratio * linkingFactor, 2);
        }

        // Gets the CPI value and its base-series name for a given month.
        // Falls back to the previous month if current month data not yet published.
        private async Task<(decimal cpi, string series)> GetCpiWithSeriesAsync(DateTime month)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var m = month.AddMonths(-attempt);
                string period = $"{m.Month:D2}-{m.Year}";
                string url = $"https://api.cbs.gov.il/index/data/price?id={CpiIndexId}&startPeriod={period}&endPeriod={period}&format=json&lang=he&download=false";

                var json = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var dates = doc.RootElement.GetProperty("month")[0].GetProperty("date");

                if (dates.GetArrayLength() > 0)
                {
                    var entry  = dates[0];
                    decimal cpi = entry.GetProperty("currBase").GetProperty("value").GetDecimal();
                    string series = entry.GetProperty("currBase").GetProperty("baseDesc").GetString() ?? "";
                    return (cpi, series);
                }
            }
            throw new InvalidOperationException($"Could not retrieve CBS CPI for {month:MM-yyyy}.");
        }

        // Computes the accumulated linking factor from fromSeries to toSeries.
        // The CBS uses biennial base-year changes ("2006 ממוצע" → "2008 ממוצע" → ... → "2024 ממוצע").
        // Each individual LF = round(annual average of the transition year, 1 decimal) / 100,
        // where that year's monthly data is still in the preceding base series.
        private async Task<decimal> GetAccumulatedLinkingFactorAsync(string fromSeries, string toSeries)
        {
            int fromYear = ExtractBaseYear(fromSeries);
            int toYear   = ExtractBaseYear(toSeries);

            if (fromYear >= toYear)
                throw new InvalidOperationException(
                    $"Base series year ({fromYear}) must be earlier than target series year ({toYear}).");

            decimal accumulated = 1m;
            int current = fromYear;

            while (current < toYear)
            {
                int next = current + 2; // CBS consistently uses 2-year base intervals
                decimal lf = await GetLinkingFactorForYearAsync(next);
                accumulated *= lf;
                current = next;
            }

            return accumulated;
        }

        // Returns the single linking factor for transitioning into the "refYear ממוצע" series.
        // = round( avg(all 12 monthly CPI values for refYear, in the preceding series), 1 decimal ) / 100
        private async Task<decimal> GetLinkingFactorForYearAsync(int refYear)
        {
            string url = $"https://api.cbs.gov.il/index/data/price?id={CpiIndexId}&startPeriod=01-{refYear}&endPeriod=12-{refYear}&format=json&lang=he&download=false&Page=1&PageSize=100";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            var dates = doc.RootElement.GetProperty("month")[0].GetProperty("date");
            decimal sum = 0m;
            int count   = 0;
            foreach (var entry in dates.EnumerateArray())
            {
                sum += entry.GetProperty("currBase").GetProperty("value").GetDecimal();
                count++;
            }

            if (count == 0)
                throw new InvalidOperationException($"No CBS CPI data found for year {refYear}.");

            // CBS publishes CPI to 1 decimal; the annual average is also rounded to 1 decimal
            decimal avg = Math.Round(sum / count, 1, MidpointRounding.AwayFromZero);
            return avg / 100m;
        }

        private static int ExtractBaseYear(string series)
        {
            var m = Regex.Match(series, @"\d{4}");
            if (!m.Success)
                throw new InvalidOperationException($"Cannot extract year from CBS series name: '{series}'");
            return int.Parse(m.Value);
        }
    }
}
