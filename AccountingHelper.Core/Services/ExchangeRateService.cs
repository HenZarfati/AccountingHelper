using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AccountingHelper.Core.Services
{
    public class ExchangeRateService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        // Returns the Bank of Israel official representative rate (שער יציג) for USD/ILS
        public async Task<decimal> GetUsdToIlsRateAsync()
        {
            var json = await _httpClient.GetStringAsync("https://boi.org.il/PublicApi/GetExchangeRates?key=USD");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                      .GetProperty("exchangeRates")[0]
                      .GetProperty("currentExchangeRate")
                      .GetDecimal();
        }
    }
}
