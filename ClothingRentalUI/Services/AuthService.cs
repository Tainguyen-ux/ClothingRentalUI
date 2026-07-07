using System.Net.Http.Json;
using ClothingRentalUI.Models.Auth;
using ClothingRentalUI.Models.Common;

namespace ClothingRentalUI.Services;

public class AuthService : IAuthService
{
    private readonly HttpClient _httpClient;

    public AuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request)
    {
        try
        {
            // Gửi yêu cầu POST đăng nhập tới API "auth/login"
            var response = await _httpClient.PostAsJsonAsync("auth/login", request);
            
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
                return result ?? new ApiResponse<LoginResponse> 
                { 
                    Success = false, 
                    Message = "Không thể giải mã dữ liệu phản hồi từ máy chủ." 
                };
            }
            
            // Xử lý các lỗi trả về từ API (ví dụ: Sai mật khẩu, tài khoản không tồn tại)
            try
            {
                var errorResult = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
                if (errorResult != null)
                {
                    return errorResult;
                }
            }
            catch
            {
                // Bỏ qua nếu response content không phải là JSON chuẩn ApiResponse
            }
            
            return new ApiResponse<LoginResponse>
            {
                Success = false,
                Message = $"Lỗi kết nối API: {response.StatusCode} ({response.ReasonPhrase})"
            };
        }
        catch (Exception ex)
        {
            return new ApiResponse<LoginResponse>
            {
                Success = false,
                Message = $"Đã xảy ra lỗi hệ thống khi gọi API: {ex.Message}"
            };
        }
    }
}
