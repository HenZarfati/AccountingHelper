using ClosedXML.Excel;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AccountingHelper.Core.Services
{
    public class ExcelProcessorService
    {
        private static readonly string[] HebrewMonths =
        {
            "ינואר", "פברואר", "מרץ", "אפריל", "מאי", "יוני",
            "יולי", "אוגוסט", "ספטמבר", "אוקטובר", "נובמבר", "דצמבר"
        };

        private readonly ExchangeRateService _exchangeRateService = new ExchangeRateService();

        public async Task<string> ProcessAsync(string inputFilePath)
        {
            decimal usdRate = await _exchangeRateService.GetUsdToIlsRateAsync();
            string hebrewMonth = HebrewMonths[DateTime.Today.Month - 1];

            using var workbook = new XLWorkbook(inputFilePath);
            var worksheet = workbook.Worksheets.First();

            int lastCol = worksheet.LastColumnUsed().ColumnNumber();
            var headerRow = worksheet.Row(1);

            int quantityCol = -1, priceCol = -1;
            for (int c = 1; c <= lastCol; c++)
            {
                var header = headerRow.Cell(c).GetString().Trim();
                if (header.Equals("Quantity", StringComparison.OrdinalIgnoreCase)) quantityCol = c;
                if (header.Equals("Price", StringComparison.OrdinalIgnoreCase)) priceCol = c;
            }

            if (quantityCol == -1)
                throw new InvalidOperationException("Could not find a 'Quantity' column in the file.");
            if (priceCol == -1)
                throw new InvalidOperationException("Could not find a 'Price' column in the file.");

            int monthCol = lastCol + 1;
            int resultCol = lastCol + 2;
            headerRow.Cell(monthCol).Value = "חודש";
            headerRow.Cell(resultCol).Value = "תוצאה";

            int lastRow = worksheet.LastRowUsed().RowNumber();
            for (int r = 2; r <= lastRow; r++)
            {
                var row = worksheet.Row(r);
                decimal quantity = row.Cell(quantityCol).GetValue<decimal>();
                decimal price = row.Cell(priceCol).GetValue<decimal>();

                row.Cell(monthCol).Value = hebrewMonth;
                row.Cell(resultCol).Value = quantity * price * usdRate;
            }

            string dir = Path.GetDirectoryName(inputFilePath)!;
            string nameWithoutExt = Path.GetFileNameWithoutExtension(inputFilePath);
            string outputPath = Path.Combine(dir, $"{nameWithoutExt}_processed.xlsx");
            workbook.SaveAs(outputPath);

            return outputPath;
        }
    }
}
