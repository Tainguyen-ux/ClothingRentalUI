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

        public string RentBankBin { get; set; } = string.Empty;
        public string RentAccountNumber { get; set; } = string.Empty;
        public string RentAccountName { get; set; } = string.Empty;

        public string DepositBankBin { get; set; } = string.Empty;
        public string DepositAccountNumber { get; set; } = string.Empty;
        public string DepositAccountName { get; set; } = string.Empty;

        public string BankBin { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string SuccessSpeech { get; set; } = string.Empty;

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
            // Legacy fallbacks
            BankBin = await GetSettingValueAsync("VietQR_BankBin");
            AccountNumber = await GetSettingValueAsync("VietQR_AccountNumber");
            AccountName = await GetSettingValueAsync("VietQR_AccountName");

            RentBankBin = await GetSettingValueAsync("VietQR_RentBankBin");
            RentAccountNumber = await GetSettingValueAsync("VietQR_RentAccountNumber");
            RentAccountName = await GetSettingValueAsync("VietQR_RentAccountName");

            if (string.IsNullOrEmpty(RentBankBin)) RentBankBin = BankBin;
            if (string.IsNullOrEmpty(RentAccountNumber)) RentAccountNumber = AccountNumber;
            if (string.IsNullOrEmpty(RentAccountName)) RentAccountName = AccountName;

            DepositBankBin = await GetSettingValueAsync("VietQR_DepositBankBin");
            DepositAccountNumber = await GetSettingValueAsync("VietQR_DepositAccountNumber");
            DepositAccountName = await GetSettingValueAsync("VietQR_DepositAccountName");

            if (string.IsNullOrEmpty(DepositBankBin)) DepositBankBin = RentBankBin;
            if (string.IsNullOrEmpty(DepositAccountNumber)) DepositAccountNumber = RentAccountNumber;
            if (string.IsNullOrEmpty(DepositAccountName)) DepositAccountName = RentAccountName;
            
            SuccessSpeech = await GetSettingValueAsync("VietQR_SuccessSpeech");
            if (string.IsNullOrWhiteSpace(SuccessSpeech))
            {
                SuccessSpeech = "Giao dịch thành công, cảm ơn quý khách";
            }
        }
    }
}
