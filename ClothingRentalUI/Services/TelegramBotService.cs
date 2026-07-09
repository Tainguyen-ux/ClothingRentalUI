using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClothingRentalUI.Services;

public class TelegramBotService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly HttpClient _httpClient;
    private long _offset = 0;

    public TelegramBotService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _httpClient = new HttpClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[TelegramBotService] Background service started.");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ClothingRentalDbContext>();

                // Đọc cấu hình Telegram
                var setting = await dbContext.SystemSettings.FirstOrDefaultAsync(s => s.Key == "TelegramBot", stoppingToken);
                if (setting != null)
                {
                    var config = DeserializeConfig(setting.ValueJson);
                    if (config != null && config.Enabled && !string.IsNullOrWhiteSpace(config.BotToken))
                    {
                        await PollUpdatesAsync(config.BotToken, dbContext, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TelegramBotService] Error in main loop: {ex.Message}");
            }

            // Chờ 3 giây trước lần quét tiếp theo
            await Task.Delay(3000, stoppingToken);
        }
    }

    private async Task PollUpdatesAsync(string token, ClothingRentalDbContext dbContext, CancellationToken stoppingToken)
    {
        try
        {
            var url = $"https://api.telegram.org/bot{token}/getUpdates?offset={_offset}&timeout=2";
            var response = await _httpClient.GetAsync(url, stoppingToken);
            if (!response.IsSuccessStatusCode) return;

            var jsonString = await response.Content.ReadAsStringAsync(stoppingToken);
            using var doc = JsonDocument.Parse(jsonString);
            var root = doc.RootElement;

            if (root.TryGetProperty("ok", out var okProp) && okProp.GetBoolean() &&
                root.TryGetProperty("result", out var resultProp) && resultProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var update in resultProp.EnumerateArray())
                {
                    var updateId = update.GetProperty("update_id").GetInt64();
                    _offset = updateId + 1;

                    if (update.TryGetProperty("message", out var msgProp) &&
                        msgProp.TryGetProperty("text", out var textProp) &&
                        msgProp.TryGetProperty("from", out var fromProp))
                    {
                        var text = textProp.GetString() ?? "";
                        var telegramUserId = fromProp.GetProperty("id").GetInt64().ToString(); // Lấy ID dạng chuỗi (long)

                        if (text.StartsWith("/start connect_", StringComparison.OrdinalIgnoreCase))
                        {
                            var userIdStr = text.Substring("/start connect_".Length).Trim();
                            if (int.TryParse(userIdStr, out var userId))
                            {
                                var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, stoppingToken);
                                if (user != null)
                                {
                                    user.TelegramId = telegramUserId;
                                    await dbContext.SaveChangesAsync(stoppingToken);

                                    // Gửi tin nhắn chào mừng và xác nhận thành công
                                    var confirmationText = $"🎉 *LIÊN KẾT TÀI KHOẢN THÀNH CÔNG!*\n\nHệ thống xác nhận đã liên kết tài khoản sau đây với Telegram:\n• *Họ tên*: {user.FullName}\n• *Tài khoản*: `{user.Username}`\n• *Email*: {user.Email}\n\nTừ bây giờ bạn sẽ nhận được các thông báo và cảnh báo từ hệ thống tại đây.";
                                    var sendUrl = $"https://api.telegram.org/bot{token}/sendMessage?chat_id={telegramUserId}&text={Uri.EscapeDataString(confirmationText)}&parse_mode=Markdown";
                                    await _httpClient.GetAsync(sendUrl, stoppingToken);

                                    Console.WriteLine($"[TelegramBotService] Successfully bound Telegram ID {telegramUserId} to User: {user.Username} (ID {userId})");
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TelegramBotService] Error polling Telegram updates: {ex.Message}");
        }
    }

    private TelegramBotConfig? DeserializeConfig(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<TelegramBotConfig>(json, options);
        }
        catch
        {
            return null;
        }
    }

    private class TelegramBotConfig
    {
        public string BotToken { get; set; } = string.Empty;
        public string ChatId { get; set; } = string.Empty;
        public bool Enabled { get; set; } = false;
    }
}
