using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;
using ClothingRentalUI.Helpers;

namespace ClothingRentalUI.Pages.Settings;

public class ProfileModel : PageModel
{
    private readonly ClothingRentalDbContext _context;
    private static readonly HttpClient _httpClient = new();

    public ProfileModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public User CurrentUser { get; set; } = null!;
    public string? BotUsername { get; set; }
    public bool TelegramEnabled { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToPage("/Auth/Login");
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user == null)
        {
            return RedirectToPage("/Auth/Login");
        }

        CurrentUser = user;

        // Lấy thông tin Bot Telegram để tạo link liên kết
        var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == "TelegramBot");
        if (setting != null)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var config = JsonSerializer.Deserialize<TelegramBotConfig>(setting.ValueJson, options);
                if (config != null && config.Enabled && !string.IsNullOrWhiteSpace(config.BotToken))
                {
                    TelegramEnabled = true;
                    // Lấy Username của Bot thông qua API getMe
                    var response = await _httpClient.GetAsync($"https://api.telegram.org/bot{config.BotToken}/getMe");
                    if (response.IsSuccessStatusCode)
                    {
                        var resBody = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(resBody);
                        if (doc.RootElement.TryGetProperty("result", out var resultObj) &&
                            resultObj.TryGetProperty("username", out var userProp))
                        {
                            BotUsername = userProp.GetString();
                        }
                    }
                }
            }
            catch
            {
                // Bỏ qua lỗi kết nối bot
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostUpdateProfileAsync(string fullName, string email, string phoneNumber)
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user == null) return RedirectToPage("/Auth/Login");

        if (string.IsNullOrWhiteSpace(fullName))
        {
            ErrorMessage = "Họ tên không được để trống.";
            return RedirectToPage();
        }

        user.FullName = fullName.Trim();
        user.Email = email?.Trim() ?? string.Empty;
        user.PhoneNumber = phoneNumber?.Trim() ?? string.Empty;

        await _context.SaveChangesAsync();

        // Cập nhật session
        HttpContext.Session.SetString("FullName", user.FullName);

        SuccessMessage = "Cập nhật thông tin cá nhân thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(string currentPassword, string newPassword, string confirmPassword)
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user == null) return RedirectToPage("/Auth/Login");

        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            ErrorMessage = "Vui lòng điền đầy đủ thông tin mật khẩu.";
            return RedirectToPage();
        }

        if (!PasswordHasher.VerifyPassword(currentPassword, user.PasswordHash))
        {
            ErrorMessage = "Mật khẩu hiện tại không chính xác.";
            return RedirectToPage();
        }

        if (newPassword != confirmPassword)
        {
            ErrorMessage = "Xác nhận mật khẩu mới không khớp.";
            return RedirectToPage();
        }

        user.PasswordHash = PasswordHasher.HashPassword(newPassword);
        await _context.SaveChangesAsync();

        SuccessMessage = "Đổi mật khẩu thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisconnectTelegramAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user == null) return RedirectToPage("/Auth/Login");

        user.TelegramId = string.Empty;
        await _context.SaveChangesAsync();

        SuccessMessage = "Đã hủy liên kết Telegram.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetCheckTelegramStatusAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return new JsonResult(new { linked = false });
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user != null && !string.IsNullOrWhiteSpace(user.TelegramId))
        {
            return new JsonResult(new { linked = true, telegramId = user.TelegramId });
        }

        return new JsonResult(new { linked = false });
    }

    private class TelegramBotConfig
    {
        public string BotToken { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty;
        public bool Enabled { get; set; } = false;
    }
}
