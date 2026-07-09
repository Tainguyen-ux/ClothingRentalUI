using ClothingRentalUI.Models.Auth;
using ClothingRentalUI.Models.Common;

namespace ClothingRentalUI.Services;

public interface IAuthService
{
    Task<ServiceResult<LoginResponse>> LoginAsync(LoginRequest request);
}

