using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using System.Text.Json;

namespace ClothingRentalUI.Pages.Orders
{
    public class CustomerDisplayModel : PageModel
    {
        private readonly ClothingRentalDbContext _context;

        public CustomerDisplayModel(ClothingRentalDbContext context)
        {
            _context = context;
        }

        public string BankBin { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;

        private async Task<string> GetSettingValueAsync(string key)
        {
            var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting == null) return "";
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var obj = JsonSerializer.Deserialize<StandardSettingJson>(setting.ValueJson, options);
                return obj?.value ?? "";
            }
            catch
            {
                return "";
            }
        }

        private class StandardSettingJson
        {
            public string value { get; set; } = string.Empty;
            public string description { get; set; } = string.Empty;
        }

        public async Task OnGetAsync()
        {
            BankBin = await GetSettingValueAsync("VietQR_BankBin");
            AccountNumber = await GetSettingValueAsync("VietQR_AccountNumber");
            AccountName = await GetSettingValueAsync("VietQR_AccountName");
        }
    }
}
