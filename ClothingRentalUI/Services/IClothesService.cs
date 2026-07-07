using ClothingRentalUI.Models.Clothes;
using ClothingRentalUI.Models.Common;

namespace ClothingRentalUI.Services;

public interface IClothesService
{
    Task<ApiResponse<IEnumerable<ClothesDto>>> GetAllAsync(string? category = null);
    Task<ApiResponse<ClothesDto>> GetByIdAsync(int id);
}
