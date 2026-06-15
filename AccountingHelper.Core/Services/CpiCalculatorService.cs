using System;
using System.Collections.Concurrent;
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

        // Historical CPI is immutable, so cache results to avoid re-fetching the same
        // month/year across rows. The target month and all linking factors are identical
        // for every row in a file — without this each row repeats ~11 slow CBS calls.
        private static readonly ConcurrentDictionary<string, (decimal cpi, string series)> _cpiCache =
            new ConcurrentDictionary<string, (decimal, string)>();
        private static readonly ConcurrentDictionary<int, (decimal avg, int baseYear)> _annualCache =
            new ConcurrentDictionary<int, (decimal, int)>();

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
        // Uses the "known index" (מדד ידוע) for each reference date (see GetKnownIndexMonth).
        // When base and target are in different CBS base-year series, accumulates
        // linking factors (avg of transition year / 100) across each series boundary.
        public async Task<decimal> CalculateIndexedAmountAsync(decimal baseAmount, DateTime baseDate, DateTime targetDate)
        {
            var baseCpiMonth   = GetKnownIndexMonth(baseDate);
            var targetCpiMonth = GetKnownIndexMonth(targetDate);

            var (baseCpi,   baseSeries)   = await GetCpiWithSeriesAsync(baseCpiMonth);
            var (targetCpi, targetSeries) = await GetCpiWithSeriesAsync(targetCpiMonth);

            decimal ratio = targetCpi / baseCpi;

            if (baseSeries == targetSeries)
                return Math.Round(baseAmount * ratio, 2);

            decimal linkingFactor = await GetAccumulatedLinkingFactorAsync(baseCpiMonth.Year, targetCpiMonth.Year);
            return Math.Round(baseAmount * ratio * linkingFactor, 2);
        }

        // Returns the month of the "known index" (מדד ידוע) for a given date.
        // The CBS publishes each month's index on the 15th of the following month, so:
        //   day >= 16 -> previous month's index is already published (use month - 1)
        //   day <= 15 -> previous month not yet published (use month - 2)
        // e.g. 16/07/2008 -> Jun 2008; 15/08/2017 -> Jun 2017.
        private static DateTime GetKnownIndexMonth(DateTime date)
            => date.Day >= 16 ? date.AddMonths(-1) : date.AddMonths(-2);

        // Gets the CPI value and its base-series name for a given month.
        // Falls back to the previous month if current month data not yet published.
        private async Task<(decimal cpi, string series)> GetCpiWithSeriesAsync(DateTime month)
        {
            string cacheKey = $"{month.Month:D2}-{month.Year}";
            if (_cpiCache.TryGetValue(cacheKey, out var cached))
                return cached;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                var m = month.AddMonths(-attempt);
                string period = $"{m.Month:D2}-{m.Year}";
                string url = $"https://api.cbs.gov.il/index/data/price?id={CpiIndexId}&startPeriod={period}&endPeriod={period}&format=json&lang=he&download=false";

                var json = await HttpRetryHelper.GetStringAsync(_http, url);
                using var doc = JsonDocument.Parse(json);
                var dates = doc.RootElement.GetProperty("month")[0].GetProperty("date");

                if (dates.GetArrayLength() > 0)
                {
                    var entry  = dates[0];
                    decimal cpi = entry.GetProperty("currBase").GetProperty("value").GetDecimal();
                    string series = entry.GetProperty("currBase").GetProperty("baseDesc").GetString() ?? "";
                    var result = (cpi, series);
                    _cpiCache[cacheKey] = result;
                    return result;
                }
            }
            throw new InvalidOperationException($"Could not retrieve CBS CPI for {month:MM-yyyy}.");
        }

        // Accumulates the CBS base-series linking factors crossed between the base month's
        // calendar year and the target month's calendar year.
        //
        // CBS base years are NOT always two apart — the sequence jumps 2002 -> 2006 (no 2004
        // base), so a fixed +2 step inserts phantom factors. Instead we read the actual
        // base-series label each calendar year's data is published in and link into every
        // distinct base we cross. The link into a base = that base year's annual average
        // (still expressed in the preceding base), rounded to 1 decimal, / 100.
        private async Task<decimal> GetAccumulatedLinkingFactorAsync(int fromCalendarYear, int toCalendarYear)
        {
            decimal accumulated = 1m;
            int? previousBaseYear = null;

            for (int year = fromCalendarYear; year <= toCalendarYear; year++)
            {
                var (_, baseYear) = await GetAnnualAsync(year);

                if (previousBaseYear == null)
                {
                    previousBaseYear = baseYear; // starting base — nothing to link yet
                    continue;
                }

                if (baseYear != previousBaseYear)
                {
                    // Crossed into a new base; its linking factor is that base year's
                    // annual average (published in the previous base).
                    var (linkAvg, _) = await GetAnnualAsync(baseYear);
                    accumulated *= linkAvg / 100m;
                    previousBaseYear = baseYear;
                }
            }

            return accumulated;
        }

        // Returns (annual average rounded to 1 decimal, base-series year) for a calendar year. Cached.
        private async Task<(decimal avg, int baseYear)> GetAnnualAsync(int year)
        {
            if (_annualCache.TryGetValue(year, out var cached))
                return cached;

            string url = $"https://api.cbs.gov.il/index/data/price?id={CpiIndexId}&startPeriod=01-{year}&endPeriod=12-{year}&format=json&lang=he&download=false&Page=1&PageSize=100";
            var json = await HttpRetryHelper.GetStringAsync(_http, url);
            using var doc = JsonDocument.Parse(json);

            var dates = doc.RootElement.GetProperty("month")[0].GetProperty("date");
            decimal sum = 0m;
            int count   = 0;
            string baseDesc = "";
            foreach (var entry in dates.EnumerateArray())
            {
                var currBase = entry.GetProperty("currBase");
                sum += currBase.GetProperty("value").GetDecimal();
                if (baseDesc.Length == 0)
                    baseDesc = currBase.GetProperty("baseDesc").GetString() ?? "";
                count++;
            }

            if (count == 0)
                throw new InvalidOperationException($"No CBS CPI data found for year {year}.");

            // CBS publishes CPI to 1 decimal; the annual average is also rounded to 1 decimal
            decimal avg = Math.Round(sum / count, 1, MidpointRounding.AwayFromZero);
            var result = (avg, ExtractBaseYear(baseDesc));
            _annualCache[year] = result;
            return result;
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
