using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

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

        // Returns target date: 25th of current month if today >= 25, else 25th of previous month.
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

        // Calculates indexed amount: baseAmount × (targetCPI / baseCPI)
        // CBS convention: uses CPI of the month BEFORE each reference date
        public async Task<decimal> CalculateIndexedAmountAsync(decimal baseAmount, DateTime baseDate, DateTime targetDate)
        {
            decimal baseCpi   = await GetCpiForMonthAsync(baseDate.AddMonths(-1));
            decimal targetCpi = await GetCpiForMonthAsync(targetDate.AddMonths(-1));
            return Math.Round(baseAmount * (targetCpi / baseCpi), 2);
        }

        private async Task<decimal> GetCpiForMonthAsync(DateTime date)
        {
            string period = $"{date.Month:D2}-{date.Year}";
            string url = $"https://api.cbs.gov.il/index/data/price?id={CpiIndexId}&startPeriod={period}&endPeriod={period}&format=xml&lang=he&download=false";

            var xml = await _http.GetStringAsync(url);
            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://www.cbs.gov.il";

            // Try to find the value element — CBS XML structure has a "VALUE" or "val" element
            decimal val = ExtractCpiValue(doc);
            if (val > 0) return val;

            // Fallback: try last available month if current month not yet published
            string fallbackPeriod = $"{date.AddMonths(-1).Month:D2}-{date.AddMonths(-1).Year}";
            string fallbackUrl = $"https://api.cbs.gov.il/index/data/price?id={CpiIndexId}&startPeriod={fallbackPeriod}&endPeriod={fallbackPeriod}&format=xml&lang=he&download=false";

            var fallbackXml = await _http.GetStringAsync(fallbackUrl);
            decimal fallbackVal = ExtractCpiValue(XDocument.Parse(fallbackXml));
            if (fallbackVal > 0) return fallbackVal;

            throw new InvalidOperationException($"Could not retrieve CPI value for {period} from CBS API.");
        }

        // CBS API returns <value>104.9</value> inside <currBase> — match case-insensitively
        private static decimal ExtractCpiValue(XDocument doc)
        {
            foreach (var el in doc.Descendants())
            {
                if (string.Equals(el.Name.LocalName, "value", StringComparison.OrdinalIgnoreCase) &&
                    decimal.TryParse(el.Value, System.Globalization.NumberStyles.Any,
                                     System.Globalization.CultureInfo.InvariantCulture, out decimal cpi) && cpi > 0)
                    return cpi;
            }
            return 0;
        }
    }
}
