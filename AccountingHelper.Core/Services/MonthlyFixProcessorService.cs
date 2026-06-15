using ClosedXML.Excel;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AccountingHelper.Core.Services
{
    public class MonthlyFixProcessorService
    {
        private static readonly string[] HebrewMonths =
        {
            "ינואר", "פברואר", "מרץ", "אפריל", "מאי", "יוני",
            "יולי", "אוגוסט", "ספטמבר", "אוקטובר", "נובמבר", "דצמבר"
        };

        private readonly ExchangeRateService _exchangeRateService = new ExchangeRateService();
        private readonly CpiCalculatorService _cpiService = new CpiCalculatorService();

        public async Task<string> ProcessAsync(string inputFilePath)
        {
            DateTime targetDate = CpiCalculatorService.GetTargetDate();

            using var workbook = new XLWorkbook(inputFilePath);
            var ws = workbook.Worksheets.First();

            // Map column headers to indices
            int lastCol = ws.LastColumnUsed().ColumnNumber();
            var headers = Enumerable.Range(1, lastCol)
                .ToDictionary(c => ws.Cell(1, c).GetString().Trim(), c => c);

            int colItemKey      = FindCol(headers, "מפתח פריט");
            int colItemName     = FindCol(headers, "שם פריט בחשבונית");
            int colCpi          = FindCol(headers, "מדד המחירים לצרכן");
            int colBaseIndex    = FindCol(headers, "מדד בסיס");
            int colIndexDate    = FindCol(headers, "תאריך למדד");
            int colPriceAfter   = FindCol(headers, "מחיר אחרי עצמדה");
            int colCurrency     = FindCol(headers, "מטבע");
            int colRate         = FindCol(headers, "שער");
            int colNotes        = FindCol(headers, "הערות להנה\"ח");

            // Cache USD rate (fetched once, reused for all $ rows)
            decimal? usdRate = null;

            int lastRow = ws.LastRowUsed().RowNumber();
            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);

                string itemKey  = colItemKey  > 0 ? row.Cell(colItemKey).GetString().Trim()  : "";
                string itemName = colItemName > 0 ? row.Cell(colItemName).GetString().Trim() : "";
                string cpiFlag  = colCpi      > 0 ? row.Cell(colCpi).GetString().Trim()      : "";
                string currency = colCurrency > 0 ? row.Cell(colCurrency).GetString().Trim() : "";

                // 1. Copy שם פריט בחשבונית → הערות להנה"ח
                if (colNotes > 0 && !string.IsNullOrEmpty(itemName))
                    row.Cell(colNotes).Value = itemName;

                // 2. Advance date in שם פריט בחשבונית if row has a number in מפתח פריט
                if (colItemName > 0 && IsNumeric(itemKey))
                {
                    string advanced = AdvanceDateInText(itemName);
                    row.Cell(colItemName).Value = advanced;
                }
                // If * in מפתח פריט — no change to שם פריט

                // 3. CPI indexation
                if (colCpi > 0 && cpiFlag == "צמוד")
                {
                    string baseIndexStr = colBaseIndex > 0 ? row.Cell(colBaseIndex).GetString().Trim() : "";
                    string indexDateStr = colIndexDate > 0 ? row.Cell(colIndexDate).GetString().Trim() : "";

                    if (decimal.TryParse(baseIndexStr, out decimal baseAmount) &&
                        TryParseHebrewDate(indexDateStr, out DateTime baseDate))
                    {
                        decimal indexed = await _cpiService.CalculateIndexedAmountAsync(baseAmount, baseDate, targetDate);
                        if (colPriceAfter > 0)
                            row.Cell(colPriceAfter).Value = indexed;
                    }
                }

                // 4. USD rate
                if (colCurrency > 0 && currency == "$")
                {
                    if (usdRate == null)
                        usdRate = await _exchangeRateService.GetUsdRateForDateAsync(targetDate);
                    if (colRate > 0)
                        row.Cell(colRate).Value = usdRate.Value;
                }
            }

            return SaveProcessedFile(workbook, targetDate);
        }

        // Saves to C:\פיקסים חודשים\{month-year}\{month-year}.xlsx
        private string SaveProcessedFile(XLWorkbook workbook, DateTime targetDate)
        {
            string monthYear = $"{HebrewMonths[targetDate.Month - 1]}-{targetDate.Year}";
            string folder = Path.Combine(@"C:\פיקסים חודשים", monthYear);
            Directory.CreateDirectory(folder);
            string outputPath = Path.Combine(folder, $"{monthYear}.xlsx");
            workbook.SaveAs(outputPath);
            return outputPath;
        }

        // Advances the first date found in the text by 1 month.
        // Supports: MM/YYYY, MM-YYYY, Hebrew month names
        private string AdvanceDateInText(string text)
        {
            // Try MM/YYYY or MM-YYYY
            var numericMatch = Regex.Match(text, @"(\d{1,2})[/\-](\d{4})");
            if (numericMatch.Success)
            {
                int month = int.Parse(numericMatch.Groups[1].Value);
                int year  = int.Parse(numericMatch.Groups[2].Value);
                var next  = new DateTime(year, month, 1).AddMonths(1);
                string replacement = $"{next.Month:D2}/{next.Year}";
                return text.Replace(numericMatch.Value, replacement);
            }

            // Try Hebrew month name
            for (int i = 0; i < HebrewMonths.Length; i++)
            {
                if (text.Contains(HebrewMonths[i]))
                {
                    // Find the year after the Hebrew month
                    var yearMatch = Regex.Match(text, HebrewMonths[i] + @"\s+(\d{4})");
                    if (yearMatch.Success)
                    {
                        int year = int.Parse(yearMatch.Groups[1].Value);
                        var next = new DateTime(year, i + 1, 1).AddMonths(1);
                        string newMonth = HebrewMonths[next.Month - 1];
                        return text.Replace(yearMatch.Value, $"{newMonth} {next.Year}");
                    }
                    else
                    {
                        // No year — just replace month name
                        int nextIndex = (i + 1) % 12;
                        return text.Replace(HebrewMonths[i], HebrewMonths[nextIndex]);
                    }
                }
            }

            return text;
        }

        // Parses dates like MM/YYYY, MM-YYYY, YYYY-MM, or Hebrew month + year
        private bool TryParseHebrewDate(string text, out DateTime result)
        {
            result = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(text)) return false;

            // MM/YYYY or MM-YYYY
            var m = Regex.Match(text.Trim(), @"^(\d{1,2})[/\-](\d{4})$");
            if (m.Success)
            {
                result = new DateTime(int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value), 1);
                return true;
            }

            // YYYY-MM
            m = Regex.Match(text.Trim(), @"^(\d{4})[/\-](\d{1,2})$");
            if (m.Success)
            {
                result = new DateTime(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), 1);
                return true;
            }

            // Hebrew month + 4-digit year
            for (int i = 0; i < HebrewMonths.Length; i++)
            {
                if (text.Contains(HebrewMonths[i]))
                {
                    var yearMatch = Regex.Match(text, @"(\d{4})");
                    if (yearMatch.Success)
                    {
                        result = new DateTime(int.Parse(yearMatch.Value), i + 1, 1);
                        return true;
                    }
                }
            }

            // Standard DateTime parse fallback
            if (DateTime.TryParse(text, out result)) return true;

            return false;
        }

        private bool IsNumeric(string value) =>
            !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value.Trim(), @"^\d+$");

        private int FindCol(System.Collections.Generic.Dictionary<string, int> headers, string name)
        {
            foreach (var key in headers.Keys)
                if (key.Trim() == name.Trim()) return headers[key];
            return -1;
        }
    }
}
