using System.Collections.Generic;
using System.Threading.Tasks;
using ClothingRentalUI.Models.Clothes;
using ClothingRentalUI.Models.Common;
using ClothingRentalUI.Data.Entities;

namespace ClothingRentalUI.Services;

public interface IClothesService
{
    Task<ApiResponse<IEnumerable<ClothesDto>>> GetAllAsync(string? category = null, string? search = null);
    Task<ApiResponse<ClothesDto>> GetByIdAsync(int id);
    Task<ApiResponse<ClothesDto>> GetByCodeAsync(string code);
    Task<ApiResponse<ClothesDto>> CreateAsync(ClothesDto dto, int categoryId, int priceListId);
    Task<ApiResponse> LiquidateAsync(int id, int quantity);
    
    // Hàm phụ phục vụ việc tạo mới / nhập kho
    Task<ApiResponse<IEnumerable<Category>>> GetCategoriesAsync();
    Task<ApiResponse<IEnumerable<PriceList>>> GetPriceListsAsync();
}
