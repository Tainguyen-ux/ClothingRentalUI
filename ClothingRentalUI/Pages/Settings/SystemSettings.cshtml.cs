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

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAdminAccessAsync("SYSTEM_PARAMETERS_VIEW");
        if (authCheck != null) return authCheck;

        var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "TelegramBot");
        Console.WriteLine($"[OnGetAsync] Raw JSON from DB: {setting?.ValueJson}");
        if (setting != null)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var config = JsonSerializer.Deserialize<TelegramBotConfig>(setting.ValueJson, options);
                if (config != null)
                {
                    TelegramConfig = config;
                    Console.WriteLine($"[OnGetAsync] Deserialized successfully: Token={TelegramConfig.BotToken}, ChatId={TelegramConfig.ChatId}, Enabled={TelegramConfig.Enabled}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OnGetAsync] Deserialization error: {ex.Message}");
                try
                {
                    using var doc = JsonDocument.Parse(setting.ValueJson);
                    var root = doc.RootElement;
                    string token = "";
                    string chatId = "";
                    bool enabled = false;

                    if (root.TryGetProperty("BotToken", out var tokenProp) || root.TryGetProperty("botToken", out tokenProp))
                        token = tokenProp.GetString() ?? "";

                    if (root.TryGetProperty("ChatId", out var chatProp) || root.TryGetProperty("chatId", out chatProp))
                        chatId = chatProp.GetString() ?? "";

                    if (root.TryGetProperty("Enabled", out var enabledProp) || root.TryGetProperty("enabled", out enabledProp))
                    {
                        if (enabledProp.ValueKind == JsonValueKind.True)
                            enabled = true;
                        else if (enabledProp.ValueKind == JsonValueKind.False)
                            enabled = false;
                        else if (enabledProp.ValueKind == JsonValueKind.String)
                            enabled = enabledProp.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
                    }

                    TelegramConfig = new TelegramBotConfig
                    {
                        BotToken = token,
                        ChatId = chatId,
                        Enabled = enabled
                    };
                    Console.WriteLine($"[OnGetAsync] Manual Parse fallback success: Token={TelegramConfig.BotToken}, ChatId={TelegramConfig.ChatId}, Enabled={TelegramConfig.Enabled}");
                }
                catch (Exception fallbackEx)
                {
                    Console.WriteLine($"[OnGetAsync] Manual Parse fallback failed: {fallbackEx.Message}");
                }
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var authCheck = await VerifyAdminAccessAsync("SYSTEM_PARAMETERS_EDIT");
        if (authCheck != null) return authCheck;

        Console.WriteLine($"[OnPostSave] Incoming: Token={TelegramConfig?.BotToken}, ChatId={TelegramConfig?.ChatId}, Enabled={TelegramConfig?.Enabled}");

        if (TelegramConfig == null)
        {
            ErrorMessage = "Dữ liệu cấu hình không hợp lệ.";
            return RedirectToPage();
        }

        var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "TelegramBot");
        if (setting == null)
        {
            setting = new SystemSetting
            {
                Key = "TelegramBot",
                ValueJson = JsonSerializer.Serialize(TelegramConfig),
                UpdatedAt = DateTime.UtcNow
            };
            _context.SystemSettings.Add(setting);
        }
        else
        {
            setting.ValueJson = JsonSerializer.Serialize(TelegramConfig);
            setting.UpdatedAt = DateTime.UtcNow;
        }

        Console.WriteLine($"[OnPostSave] Saving JSON: {setting.ValueJson}");
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
