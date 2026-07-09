using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Helpers;
using ClothingRentalUI.Models.Auth;
using ClothingRentalUI.Models.Common;

namespace ClothingRentalUI.Services;

public class AuthService : IAuthService
{
    private readonly ClothingRentalDbContext _dbContext;

    public AuthService(ClothingRentalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return new ApiResponse<LoginResponse>
                {
                    Success = false,
                    Message = "Tên đăng nhập và mật khẩu không được để trống."
                };
            }

            var user = await _dbContext.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());

            if (user == null || !PasswordHasher.VerifyPassword(request.Password, user.PasswordHash))
            {
                return new ApiResponse<LoginResponse>
                {
                    Success = false,
                    Message = "Tên đăng nhập hoặc mật khẩu không chính xác."
                };
            }

            // Sinh token ngẫu nhiên mô phỏng phiên làm việc
            var mockToken = Guid.NewGuid().ToString("N") + "." + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(user.Username));

            return new ApiResponse<LoginResponse>
            {
                Success = true,
                Message = "Đăng nhập thành công.",
                Data = new LoginResponse
                {
                    Token = mockToken,
                    Username = user.Username,
                    FullName = user.Username == "admin" ? "Quản trị viên" : "Nhân viên cửa hàng",
                    Role = user.Role,
                    Expiration = DateTime.Now.AddHours(2)
                }
            };
        }
        catch (Exception ex)
        {
            return new ApiResponse<LoginResponse>
            {
                Success = false,
                Message = $"Đã xảy ra lỗi khi đăng nhập: {ex.Message}"
            };
        }
    }
}
