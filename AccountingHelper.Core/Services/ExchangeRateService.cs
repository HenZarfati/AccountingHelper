using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AccountingHelper.Core.Services
{
    public class ExchangeRateService
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public async Task<decimal> GetUsdToIlsRateAsync()
        {
            var json = await _httpClient.GetStringAsync("https://api.frankfurter.app/latest?from=USD&to=ILS");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("rates").GetProperty("ILS").GetDecimal();
        }
    }
}
