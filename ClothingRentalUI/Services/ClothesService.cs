using System.Net.Http.Json;
using ClothingRentalUI.Models.Clothes;
using ClothingRentalUI.Models.Common;

namespace ClothingRentalUI.Services;

public class ClothesService : IClothesService
{
    private readonly HttpClient _httpClient;

    public HttpClient Client => _httpClient;

    public ClothesService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ApiResponse<IEnumerable<ClothesDto>>> GetAllAsync(string? category = null)
    {
        try
        {
            var url = "clothes";
            if (!string.IsNullOrEmpty(category))
            {
                url += $"?category={Uri.EscapeDataString(category)}";
            }

            var response = await _httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ApiResponse<IEnumerable<ClothesDto>>>();
                return result ?? new ApiResponse<IEnumerable<ClothesDto>> 
                { 
                    Success = false, 
                    Message = "Không thể giải mã dữ liệu danh sách sản phẩm." 
                };
            }

            try
            {
                var errorResult = await response.Content.ReadFromJsonAsync<ApiResponse<IEnumerable<ClothesDto>>>();
                if (errorResult != null) return errorResult;
            }
            catch { }

            return new ApiResponse<IEnumerable<ClothesDto>>
            {
                Success = false,
                Message = $"Lỗi kết nối API: {response.StatusCode} ({response.ReasonPhrase})"
            };
        }
        catch (Exception ex)
        {
            return new ApiResponse<IEnumerable<ClothesDto>>
            {
                Success = false,
                Message = $"Đã xảy ra lỗi hệ thống: {ex.Message}"
            };
        }
    }

    public async Task<ApiResponse<ClothesDto>> GetByIdAsync(int id)
    {
        try
        {
            var response = await _httpClient.GetAsync($"clothes/{id}");
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ApiResponse<ClothesDto>>();
                return result ?? new ApiResponse<ClothesDto> 
                { 
                    Success = false, 
                    Message = "Không thể giải mã dữ liệu chi tiết sản phẩm." 
                };
            }

            try
            {
                var errorResult = await response.Content.ReadFromJsonAsync<ApiResponse<ClothesDto>>();
                if (errorResult != null) return errorResult;
            }
            catch { }

            return new ApiResponse<ClothesDto>
            {
                Success = false,
                Message = $"Lỗi kết nối API: {response.StatusCode} ({response.ReasonPhrase})"
            };
        }
        catch (Exception ex)
        {
            return new ApiResponse<ClothesDto>
            {
                Success = false,
                Message = $"Đã xảy ra lỗi hệ thống: {ex.Message}"
            };
        }
    }
}
