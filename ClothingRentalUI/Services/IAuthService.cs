using ClothingRentalUI.Models.Auth;
using ClothingRentalUI.Models.Common;

namespace ClothingRentalUI.Services;

public interface IAuthService
{
    Task<ApiResponse<LoginResponse>> LoginAsync(LoginRequest request);
}
