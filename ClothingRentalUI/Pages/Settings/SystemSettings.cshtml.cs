using System;
using System.Linq;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;

namespace ClothingRentalUI.Pages.Settings;

public class SystemSettingsModel : PageModel
{
    private readonly ClothingRentalDbContext _context;
    private static readonly HttpClient _httpClient = new();

    public SystemSettingsModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public TelegramBotConfig TelegramConfig { get; set; } = new();

    [BindProperty]
    public GoogleDriveConfig DriveConfig { get; set; } = new();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public class TelegramBotConfig
    {
        public string BotToken { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty;
        public bool Enabled { get; set; } = false;
    }

    public class GoogleDriveConfig
    {
        public string FolderId { get; set; } = string.Empty;
        public string UploadUrl { get; set; } = string.Empty;
    }

    public class VietQRConfig
    {
        public string BankBin { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string SuccessSpeech { get; set; } = string.Empty;
    }

    [BindProperty]
    public VietQRConfig VietQR { get; set; } = new();

    public class BarcodeConfig
    {
        public int Width { get; set; } = 2;
        public int Height { get; set; } = 60;
        public int FontSize { get; set; } = 16;
    }

    [BindProperty]
    public BarcodeConfig BarcodePrintConfig { get; set; } = new();

    public class ShopConfig
    {
        public string ShopName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    [BindProperty]
    public ShopConfig Shop { get; set; } = new();

    public class RentalRuleConfig
    {
        public decimal LateFeePerDay { get; set; } = 10000;
        public int PenaltyBasePriceThresholdDays { get; set; } = 4;
    }

    [BindProperty]
    public RentalRuleConfig RentalRule { get; set; } = new();

    public class PrintConfig
    {
        public string RentalWidth { get; set; } = "80mm";
        public string InvoiceWidth { get; set; } = "80mm";
    }

    [BindProperty]
    public PrintConfig PrintSetting { get; set; } = new();

    public class StandardSettingJson
    {
        public string value { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
    }

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

    private async Task SaveSettingValueAsync(string key, string value, string defaultDescription)
    {
        var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
        var jsonObj = new StandardSettingJson { value = value, description = defaultDescription };
        string jsonStr = JsonSerializer.Serialize(jsonObj);

        if (setting == null) 
        {
            _context.SystemSettings.Add(new SystemSetting { Key = key, ValueJson = jsonStr, UpdatedAt = DateTime.UtcNow });
        } 
        else 
        {
            setting.ValueJson = jsonStr;
            setting.UpdatedAt = DateTime.UtcNow;
        }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAdminAccessAsync("SYSTEM_PARAMETERS_VIEW");
        if (authCheck != null) return authCheck;

        TelegramConfig.BotToken = await GetSettingValueAsync("Telegram_BotToken");
        TelegramConfig.ChatId = await GetSettingValueAsync("Telegram_ChatId");
        
        var enabledStr = await GetSettingValueAsync("Telegram_Enabled");
        TelegramConfig.Enabled = string.Equals(enabledStr, "true", StringComparison.OrdinalIgnoreCase);

        DriveConfig.FolderId = await GetSettingValueAsync("GoogleDrive_FolderId");
        DriveConfig.UploadUrl = await GetSettingValueAsync("GoogleAppScript_UploadUrl");

        VietQR.BankBin = await GetSettingValueAsync("VietQR_BankBin");
        VietQR.AccountNumber = await GetSettingValueAsync("VietQR_AccountNumber");
        VietQR.AccountName = await GetSettingValueAsync("VietQR_AccountName");
        VietQR.SuccessSpeech = await GetSettingValueAsync("VietQR_SuccessSpeech");
        if (string.IsNullOrWhiteSpace(VietQR.SuccessSpeech))
        {
            VietQR.SuccessSpeech = "Giao dịch thành công, cảm ơn quý khách";
        }

        int.TryParse(await GetSettingValueAsync("Barcode_Width") ?? "2", out int w);
        int.TryParse(await GetSettingValueAsync("Barcode_Height") ?? "60", out int h);
        int.TryParse(await GetSettingValueAsync("Barcode_FontSize") ?? "16", out int fs);
        
        BarcodePrintConfig.Width = w > 0 ? w : 2;
        BarcodePrintConfig.Height = h > 0 ? h : 60;
        BarcodePrintConfig.FontSize = fs > 0 ? fs : 16;

        Shop.ShopName = await GetSettingValueAsync("Shop_Name");
        Shop.Address = await GetSettingValueAsync("Shop_Address");
        Shop.PhoneNumber = await GetSettingValueAsync("Shop_PhoneNumber");
        Shop.Notes = await GetSettingValueAsync("Shop_Notes");

        if (string.IsNullOrWhiteSpace(Shop.ShopName)) Shop.ShopName = "CLOTHING RENTAL SHOP";
        if (string.IsNullOrWhiteSpace(Shop.Address)) Shop.Address = "123 Đường ABC, Quận XYZ, TP. Hồ Chí Minh";
        if (string.IsNullOrWhiteSpace(Shop.PhoneNumber)) Shop.PhoneNumber = "0901234567";
        if (string.IsNullOrWhiteSpace(Shop.Notes)) Shop.Notes = "Cảm ơn quý khách đã tin tưởng và ủng hộ!";

        var lfdStr = await GetSettingValueAsync("Rental_LateFeePerDay");
        if (decimal.TryParse(lfdStr, out decimal lfd)) RentalRule.LateFeePerDay = lfd;
        else RentalRule.LateFeePerDay = 10000;

        var ldtStr = await GetSettingValueAsync("Rental_LateDayThreshold");
        if (int.TryParse(ldtStr, out int ldt)) RentalRule.PenaltyBasePriceThresholdDays = ldt;
        else RentalRule.PenaltyBasePriceThresholdDays = 4;

        PrintSetting.RentalWidth = await GetSettingValueAsync("Print_RentalWidth");
        if (string.IsNullOrWhiteSpace(PrintSetting.RentalWidth)) PrintSetting.RentalWidth = "80mm";

        PrintSetting.InvoiceWidth = await GetSettingValueAsync("Print_InvoiceWidth");
        if (string.IsNullOrWhiteSpace(PrintSetting.InvoiceWidth)) PrintSetting.InvoiceWidth = "80mm";

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var authCheck = await VerifyAdminAccessAsync("SYSTEM_PARAMETERS_EDIT");
        if (authCheck != null) return authCheck;

        if (TelegramConfig == null)
        {
            ErrorMessage = "Dữ liệu cấu hình không hợp lệ.";
            return RedirectToPage();
        }

        await SaveSettingValueAsync("Telegram_BotToken", TelegramConfig.BotToken ?? "", "Token của Telegram Bot");
        await SaveSettingValueAsync("Telegram_ChatId", TelegramConfig.ChatId ?? "", "ID của nhóm chat Telegram");
        await SaveSettingValueAsync("Telegram_Enabled", TelegramConfig.Enabled ? "true" : "false", "Kích hoạt gửi thông báo qua Telegram (true/false)");
        
        if (DriveConfig != null)
        {
            await SaveSettingValueAsync("GoogleDrive_FolderId", DriveConfig.FolderId ?? "", "ID của thư mục Google Drive để lưu trữ hình ảnh");
            await SaveSettingValueAsync("GoogleAppScript_UploadUrl", DriveConfig.UploadUrl ?? "", "Địa chỉ API Google Apps Script dùng để Upload Ảnh");
        }

        if (BarcodePrintConfig != null)
        {
            await SaveSettingValueAsync("Barcode_Width", BarcodePrintConfig.Width.ToString(), "Độ rộng nét in mã vạch (px)");
            await SaveSettingValueAsync("Barcode_Height", BarcodePrintConfig.Height.ToString(), "Chiều cao mã vạch (px)");
            await SaveSettingValueAsync("Barcode_FontSize", BarcodePrintConfig.FontSize.ToString(), "Cỡ chữ của mã vạch (px)");
        }

        if (VietQR != null)
        {
            await SaveSettingValueAsync("VietQR_BankBin", VietQR.BankBin ?? "", "Mã BIN ngân hàng VietQR");
            await SaveSettingValueAsync("VietQR_AccountNumber", VietQR.AccountNumber ?? "", "Số tài khoản ngân hàng VietQR");
            await SaveSettingValueAsync("VietQR_AccountName", VietQR.AccountName ?? "", "Tên chủ tài khoản ngân hàng VietQR");
            await SaveSettingValueAsync("VietQR_SuccessSpeech", VietQR.SuccessSpeech ?? "Giao dịch thành công, cảm ơn quý khách", "Câu nói khi hoàn tất giao dịch");
        }

        if (Shop != null)
        {
            await SaveSettingValueAsync("Shop_Name", Shop.ShopName ?? "CLOTHING RENTAL SHOP", "Tên cửa hàng");
            await SaveSettingValueAsync("Shop_Address", Shop.Address ?? "", "Địa chỉ cửa hàng");
            await SaveSettingValueAsync("Shop_PhoneNumber", Shop.PhoneNumber ?? "", "Số điện thoại cửa hàng");
            await SaveSettingValueAsync("Shop_Notes", Shop.Notes ?? "", "Lời nhắn/Ghi chú chân hóa đơn");
        }

        if (RentalRule != null)
        {
            await SaveSettingValueAsync("Rental_LateFeePerDay", RentalRule.LateFeePerDay.ToString("F0"), "Phí trễ hạn mỗi ngày (VND)");
            await SaveSettingValueAsync("Rental_LateDayThreshold", RentalRule.PenaltyBasePriceThresholdDays.ToString(), "Số ngày trễ hạn tối đa để tính cộng thêm giá thuê gốc");
        }

        if (PrintSetting != null)
        {
            await SaveSettingValueAsync("Print_RentalWidth", PrintSetting.RentalWidth ?? "80mm", "Kích thước/Chiều rộng in phiếu thuê");
            await SaveSettingValueAsync("Print_InvoiceWidth", PrintSetting.InvoiceWidth ?? "80mm", "Kích thước/Chiều rộng in hóa đơn");
        }

        await _context.SaveChangesAsync();
        SuccessMessage = "Lưu cấu hình tham số hệ thống thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestConnectionAsync()
    {
        var authCheck = await VerifyAdminAccessAsync("SYSTEM_PARAMETERS_EDIT");
        if (authCheck != null) return authCheck;

        if (string.IsNullOrWhiteSpace(TelegramConfig.BotToken) || string.IsNullOrWhiteSpace(TelegramConfig.ChatId))
        {
            ErrorMessage = "Vui lòng nhập đầy đủ Bot Token và Chat ID để kiểm thử kết nối.";
            return RedirectToPage();
        }

        try
        {
            var message = $"🔔 *Kiểm tra kết nối thành công!*\nHệ thống Quản lý Thuê trang phục đã kết nối thành công với Bot Telegram.\nThời gian: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
            var url = $"https://api.telegram.org/bot{TelegramConfig.BotToken}/sendMessage?chat_id={TelegramConfig.ChatId}&text={Uri.EscapeDataString(message)}&parse_mode=Markdown";
            
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                SuccessMessage = "Gửi tin nhắn thử nghiệm thành công! Hãy kiểm tra ứng dụng Telegram của bạn.";
            }
            else
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                ErrorMessage = $"Telegram API trả về lỗi: {response.StatusCode}. Chi tiết: {errorBody}";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Lỗi khi kết nối tới Telegram: {ex.Message}";
        }

        return RedirectToPage();
    }

    private async Task<IActionResult?> VerifyAdminAccessAsync(string permissionCode)
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToPage("/Auth/Login");
        }

        var hasPermission = await _context.Users
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                           u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == permissionCode));

        if (!hasPermission)
        {
            return RedirectToPage("/Clothes/Index");
        }

        return null;
    }
}
