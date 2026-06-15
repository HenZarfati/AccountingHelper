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

        // Returns target date: 25th of current month, or Sunday the 26th if 25th is Saturday
        public static DateTime GetTargetDate()
        {
            var today = DateTime.Today;
            var target = new DateTime(today.Year, today.Month, 25);
            if (target.DayOfWeek == DayOfWeek.Saturday)
                target = target.AddDays(1);
            return target;
        }

        // Calculates indexed amount: baseAmount × (targetCPI / baseCPI)
        public async Task<decimal> CalculateIndexedAmountAsync(decimal baseAmount, DateTime baseDate, DateTime targetDate)
        {
            decimal baseCpi = await GetCpiForMonthAsync(baseDate);
            decimal targetCpi = await GetCpiForMonthAsync(targetDate);
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
            foreach (var el in doc.Descendants())
            {
                if ((el.Name.LocalName == "VALUE" || el.Name.LocalName == "val" || el.Name.LocalName == "Value") &&
                    decimal.TryParse(el.Value, out decimal cpi) && cpi > 0)
                    return cpi;
            }

            // Fallback: try last available month if current month not yet published
            string fallbackPeriod = $"{date.AddMonths(-1).Month:D2}-{date.AddMonths(-1).Year}";
            string fallbackUrl = $"https://api.cbs.gov.il/index/data/price?id={CpiIndexId}&startPeriod={fallbackPeriod}&endPeriod={fallbackPeriod}&format=xml&lang=he&download=false";

            var fallbackXml = await _http.GetStringAsync(fallbackUrl);
            var fallbackDoc = XDocument.Parse(fallbackXml);

            foreach (var el in fallbackDoc.Descendants())
            {
                if ((el.Name.LocalName == "VALUE" || el.Name.LocalName == "val" || el.Name.LocalName == "Value") &&
                    decimal.TryParse(el.Value, out decimal cpi) && cpi > 0)
                    return cpi;
            }

            throw new InvalidOperationException($"Could not retrieve CPI value for {period} from CBS API.");
        }
    }
}
