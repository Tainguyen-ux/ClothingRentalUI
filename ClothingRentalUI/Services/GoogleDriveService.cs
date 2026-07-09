using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ClothingRentalUI.Services;

public class GoogleDriveUploadResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("folder")]
    public GoogleDriveFolder? Folder { get; set; }

    [JsonPropertyName("file")]
    public GoogleDriveFile? File { get; set; }

    [JsonPropertyName("uploadedAt")]
    public DateTime? UploadedAt { get; set; }

    [JsonPropertyName("webApp")]
    public string? WebApp { get; set; }
}

public class GoogleDriveFolder
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class GoogleDriveFile
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("viewUrl")]
    public string? ViewUrl { get; set; }

    [JsonPropertyName("previewUrl")]
    public string? PreviewUrl { get; set; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("directDownloadUrl")]
    public string? DirectDownloadUrl { get; set; }

    [JsonPropertyName("openUrl")]
    public string? OpenUrl { get; set; }

    [JsonPropertyName("thumbnailUrl")]
    public string? ThumbnailUrl { get; set; }

    [JsonPropertyName("driveUrl")]
    public string? DriveUrl { get; set; }
}

public interface IGoogleDriveService
{
    /// <summary>
    /// Gọi API tới Google Apps Script để upload file.
    /// </summary>
    /// <param name="folder">Tên thư mục (hoặc Folder ID tùy theo logic trong Google Script) lưu trữ.</param>
    /// <param name="fileName">Tên file.</param>
    /// <param name="mimeType">Kiểu định dạng file (vd: image/png, image/jpeg).</param>
    /// <param name="base64Data">Dữ liệu Base64 của file (đã bỏ phần tiền tố như data:image/png;base64,).</param>
    /// <returns>Đối tượng chứa toàn bộ thông tin URL của file trên Google Drive, hoặc null nếu thất bại.</returns>
    Task<GoogleDriveUploadResponse?> UploadFileAsync(string folder, string fileName, string mimeType, string base64Data);
}

public class GoogleDriveService : IGoogleDriveService
{
    private readonly HttpClient _httpClient;
    
    // URL Google Apps Script Web App của bạn
    private const string AppScriptUrl = "https://script.google.com/macros/s/AKfycbxxxxxxxxxxxxxxxxxxxxxxxx/exec";

    public GoogleDriveService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<GoogleDriveUploadResponse?> UploadFileAsync(string folder, string fileName, string mimeType, string base64Data)
    {
        try
        {
            var payload = new
            {
                folder = folder,
                fileName = fileName,
                mimeType = mimeType,
                base64 = base64Data
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // HttpClient mặc định tự động follow các HTTP 302 redirects (tương đương cờ --location trong curl)
            var response = await _httpClient.PostAsync(AppScriptUrl, content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[GoogleDriveService] Upload success. Response: {responseString}");
                
                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<GoogleDriveUploadResponse>(responseString, options);
                    return result;
                }
                catch (Exception jsonEx)
                {
                    Console.WriteLine($"[GoogleDriveService] Failed to deserialize response: {jsonEx.Message}");
                    return null;
                }
            }
            else
            {
                Console.WriteLine($"[GoogleDriveService] Upload failed. Status Code: {response.StatusCode}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GoogleDriveService] Upload exception: {ex.Message}");
            return null;
        }
    }
}
