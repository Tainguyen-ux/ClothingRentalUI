using System.Collections.Generic;
using System.Threading.Tasks;
using ClothingRentalUI.Models.Clothes;
using ClothingRentalUI.Models.Common;
using ClothingRentalUI.Data.Entities;

namespace ClothingRentalUI.Services;

public interface IClothesService
{
    Task<ServiceResult<IEnumerable<ClothesDto>>> GetAllAsync(string? category = null, string? search = null);
    Task<ServiceResult<ClothesDto>> GetByIdAsync(int id);
    Task<ServiceResult<ClothesDto>> GetByCodeAsync(string code);
    Task<ServiceResult<ClothesDto>> CreateAsync(ClothesDto dto, int categoryId, int priceListId);
    Task<ServiceResult> LiquidateAsync(int id, int quantity);
    
    // Hàm phụ phục vụ việc tạo mới / nhập kho
    Task<ServiceResult<IEnumerable<Category>>> GetCategoriesAsync();
    Task<ServiceResult<IEnumerable<PriceList>>> GetPriceListsAsync();
}

