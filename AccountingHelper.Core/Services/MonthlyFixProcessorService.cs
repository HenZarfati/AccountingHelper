using ClosedXML.Excel;
using System;
using System.Collections.Generic;
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

        private static readonly string[] EnglishMonthsFull =
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        };

        private static readonly string[] EnglishMonthsAbbr =
        {
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        };

        private readonly ExchangeRateService _exchangeRateService = new ExchangeRateService();
        private readonly CpiCalculatorService _cpiService = new CpiCalculatorService();

        public async Task<string> ProcessAsync(string inputFilePath)
        {
            DateTime targetDate = CpiCalculatorService.GetTargetDate();

            using var workbook = new XLWorkbook(inputFilePath);
            var ws = workbook.Worksheets.First();

            // Map column headers to indices (strip invisible Unicode direction markers Excel embeds in Hebrew text)
            int lastCol = ws.LastColumnUsed().ColumnNumber();
            var headers = Enumerable.Range(1, lastCol)
                .ToDictionary(c => StripInvisible(ws.Cell(1, c).GetString()), c => c);

            int colItemKey      = FindCol(headers, "מפתח פריט");
            int colAccountKey   = FindCol(headers, "מפתח חשבון");
            int colItemName     = FindCol(headers, "שם פריט בחשבונית");
            int colCpi          = FindCol(headers, "מדד המחירים לצרכן");
            int colBaseIndex    = FindCol(headers, "מדד בסיס");
            int colIndexDate    = FindCol(headers, "תאריך למדד");
            // Try both spellings — "הצמדה" is correct Hebrew; "עצמדה" was an earlier typo
            int colPriceAfter   = FindCol(headers, "מחיר אחרי הצמדה");
            if (colPriceAfter < 0) colPriceAfter = FindCol(headers, "מחיר אחרי עצמדה");
            int colCurrency     = FindCol(headers, "מטבע (₪ או $)");
            int colRate         = FindCol(headers, "שער");
            int colNotes        = FindCol(headers, "הערות להנה\"ח");
            if (colNotes < 0) colNotes = FindCol(headers, "הערות להנה'ח");

            // Surface missing required columns as a clear error
            var missing = new List<string>();
            if (colItemName   < 0) missing.Add("שם פריט בחשבונית");
            if (colCpi        < 0) missing.Add("מדד המחירים לצרכן");
            if (colPriceAfter < 0) missing.Add("מחיר אחרי הצמדה");
            if (colCurrency   < 0) missing.Add("מטבע (₪ או $)");
            if (colRate       < 0) missing.Add("שער");
            if (missing.Count > 0)
                throw new InvalidOperationException($"העמודות הבאות לא נמצאו בקובץ: {string.Join(", ", missing)}");


            // Pre-scan: find the "הצמדה למדד" target row (מפתח חשבון=22211 AND שם פריט=הצמדה למדד)
            int lastRow = ws.LastRowUsed().RowNumber();
            int indexationTargetRow = -1;
            if (colAccountKey > 0 && colItemName > 0)
            {
                for (int r = 2; r <= lastRow; r++)
                {
                    string ak = ws.Row(r).Cell(colAccountKey).GetString().Trim();
                    string nm = ws.Row(r).Cell(colItemName).GetString().Trim();
                    if (ak == "22211" && nm == "הצמדה למדד")
                    {
                        indexationTargetRow = r;
                        break;
                    }
                }
            }

            // Cache USD rate (fetched once, reused for all $ rows)
            decimal? usdRate = null;

            for (int r = 2; r <= lastRow; r++)
            {
                var row = ws.Row(r);

                string itemKey    = colItemKey    > 0 ? StripInvisible(row.Cell(colItemKey).GetString())    : "";
                string accountKey = colAccountKey > 0 ? StripInvisible(row.Cell(colAccountKey).GetString()) : "";
                string itemName   = colItemName   > 0 ? StripInvisible(row.Cell(colItemName).GetString())   : "";
                string cpiFlag    = colCpi        > 0 ? StripInvisible(row.Cell(colCpi).GetString())        : "";
                string currency   = colCurrency   > 0 ? StripInvisible(row.Cell(colCurrency).GetString())   : "";

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

                        // If מפתח חשבון = 22211, write result to the "הצמדה למדד" row instead of current row
                        if (accountKey == "22211" && indexationTargetRow > 0 && colPriceAfter > 0)
                            ws.Row(indexationTargetRow).Cell(colPriceAfter).Value = indexed;
                        else if (colPriceAfter > 0)
                            row.Cell(colPriceAfter).Value = indexed;
                    }
                }

                // 4. USD rate
                if (colCurrency > 0 && currency.Contains("$"))
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
        // Supports: MM/YYYY, MM-YYYY, Hebrew month names, English full names, English 3-letter abbreviations
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
                        int nextIndex = (i + 1) % 12;
                        return text.Replace(HebrewMonths[i], HebrewMonths[nextIndex]);
                    }
                }
            }

            // Try English month name (full names checked before abbreviations so "March" beats "Mar")
            string engPattern = string.Join("|",
                EnglishMonthsFull.Concat(EnglishMonthsAbbr)
                                 .Distinct()
                                 .OrderByDescending(m => m.Length));
            var engMatch = Regex.Match(text, engPattern, RegexOptions.IgnoreCase);
            if (engMatch.Success)
            {
                string matched = engMatch.Value;
                bool useAbbr = matched.Length == 3;

                int monthIdx = -1;
                for (int i = 0; i < 12; i++)
                {
                    if (string.Equals(matched, EnglishMonthsFull[i], StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(matched, EnglishMonthsAbbr[i], StringComparison.OrdinalIgnoreCase))
                    {
                        monthIdx = i;
                        break;
                    }
                }

                if (monthIdx >= 0)
                {
                    // Try to find a year after the month name to also advance the year
                    var yearMatch = Regex.Match(text, Regex.Escape(matched) + @"\s+(\d{4})", RegexOptions.IgnoreCase);
                    int nextMonth, nextYear;
                    if (yearMatch.Success)
                    {
                        int year = int.Parse(yearMatch.Groups[1].Value);
                        var next = new DateTime(year, monthIdx + 1, 1).AddMonths(1);
                        nextMonth = next.Month;
                        nextYear  = next.Year;
                        string nextName = useAbbr ? EnglishMonthsAbbr[nextMonth - 1] : EnglishMonthsFull[nextMonth - 1];
                        nextName = ApplyEnglishCase(matched, nextName);
                        return text.Substring(0, yearMatch.Index) + $"{nextName} {nextYear}" + text.Substring(yearMatch.Index + yearMatch.Length);
                    }
                    else
                    {
                        int nextIdx = (monthIdx + 1) % 12;
                        string nextName = useAbbr ? EnglishMonthsAbbr[nextIdx] : EnglishMonthsFull[nextIdx];
                        nextName = ApplyEnglishCase(matched, nextName);
                        return text.Substring(0, engMatch.Index) + nextName + text.Substring(engMatch.Index + engMatch.Length);
                    }
                }
            }

            return text;
        }

        // Matches the casing style of the original English month token onto the replacement
        private string ApplyEnglishCase(string original, string replacement)
        {
            if (original == original.ToUpperInvariant()) return replacement.ToUpperInvariant();
            if (original == original.ToLowerInvariant()) return replacement.ToLowerInvariant();
            // Title case (first letter upper, rest lower) — default for month names
            return char.ToUpper(replacement[0]) + replacement.Substring(1).ToLower();
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

        private int FindCol(Dictionary<string, int> headers, string name)
        {
            string normalized = StripInvisible(name);
            foreach (var key in headers.Keys)
                if (key == normalized) return headers[key];
            return -1;
        }

        // Strips invisible Unicode direction/formatting markers Excel embeds in RTL text
        private static string StripInvisible(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return new string(s.Where(c =>
                c != '‏' && // RIGHT-TO-LEFT MARK
                c != '‎' && // LEFT-TO-RIGHT MARK
                c != '​' && // ZERO WIDTH SPACE
                c != '‪' && // LEFT-TO-RIGHT EMBEDDING
                c != '‫' && // RIGHT-TO-LEFT EMBEDDING
                c != '‬' && // POP DIRECTIONAL FORMATTING
                c != '﻿'    // BYTE ORDER MARK
            ).ToArray()).Trim();
        }
    }
}
