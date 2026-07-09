using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;
using ClothingRentalUI.Models.Clothes;
using ClothingRentalUI.Models.Common;

namespace ClothingRentalUI.Services;

public class ClothesService : IClothesService
{
    private readonly ClothingRentalDbContext _dbContext;

    public ClothesService(ClothingRentalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ServiceResult<IEnumerable<ClothesDto>>> GetAllAsync(string? category = null, string? search = null)
    {
        try
        {
            var query = _dbContext.Products
                .Include(p => p.PriceList)
                .AsQueryable();

            // Loại bỏ các sản phẩm đã thanh lý hoàn toàn (hoặc có thể hiển thị tùy nhu cầu, ở đây ẩn đi)
            query = query.Where(p => !p.IsLiquidated);

            if (!string.IsNullOrEmpty(category))
            {
                // Truy vấn loại danh mục
                var cat = await _dbContext.Categories
                    .FirstOrDefaultAsync(c => c.Name.ToLower() == category.ToLower());
                
                if (cat != null)
                {
                    // Lọc theo prefix mã hàng vì prefix tương ứng với loại danh mục
                    query = query.Where(p => p.Code.StartsWith(cat.CodePrefix));
                }
            }

            if (!string.IsNullOrEmpty(search))
            {
                search = search.Trim().ToLower();
                query = query.Where(p => p.Name.ToLower().Contains(search) 
                                      || p.Code.ToLower().Contains(search));
            }

            var list = await query.Select(p => new ClothesDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Description = p.Description ?? string.Empty,
                ImageUrl = p.ImageUrl ?? string.Empty,
                PricePerDay = p.PriceList != null ? p.PriceList.PricePerDay : 0,
                Deposit = p.PriceList != null ? p.PriceList.Deposit : 0,
                Size = p.Size ?? string.Empty,
                Color = p.Color ?? string.Empty,
                StockQuantity = p.StockQuantity,
                IsAvailable = p.IsAvailable && p.StockQuantity > 0,
                CategoryName = _dbContext.Categories
                                   .Where(c => p.Code.StartsWith(c.CodePrefix))
                                   .Select(c => c.Name)
                                   .FirstOrDefault() ?? "Khác",
                ImportPrice = p.ImportPrice,
                TotalRentRevenue = p.TotalRentRevenue,
                IsLiquidated = p.IsLiquidated
            }).ToListAsync();

            return new ServiceResult<IEnumerable<ClothesDto>>
            {
                Success = true,
                Data = list
            };
        }
        catch (Exception ex)
        {
            return new ServiceResult<IEnumerable<ClothesDto>>
            {
                Success = false,
                Message = $"Lỗi lấy danh sách sản phẩm: {ex.Message}"
            };
        }
    }

    public async Task<ServiceResult<ClothesDto>> GetByIdAsync(int id)
    {
        try
        {
            var p = await _dbContext.Products
                .Include(prod => prod.PriceList)
                .FirstOrDefaultAsync(prod => prod.Id == id);

            if (p == null)
            {
                return new ServiceResult<ClothesDto> { Success = false, Message = "Không tìm thấy sản phẩm." };
            }

            var categoryName = await _dbContext.Categories
                .Where(c => p.Code.StartsWith(c.CodePrefix))
                .Select(c => c.Name)
                .FirstOrDefaultAsync() ?? "Khác";

            var dto = new ClothesDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Description = p.Description ?? string.Empty,
                ImageUrl = p.ImageUrl ?? string.Empty,
                PricePerDay = p.PriceList != null ? p.PriceList.PricePerDay : 0,
                Deposit = p.PriceList != null ? p.PriceList.Deposit : 0,
                Size = p.Size ?? string.Empty,
                Color = p.Color ?? string.Empty,
                StockQuantity = p.StockQuantity,
                IsAvailable = p.IsAvailable && p.StockQuantity > 0,
                CategoryName = categoryName,
                ImportPrice = p.ImportPrice,
                TotalRentRevenue = p.TotalRentRevenue,
                IsLiquidated = p.IsLiquidated
            };

            return new ServiceResult<ClothesDto> { Success = true, Data = dto };
        }
        catch (Exception ex)
        {
            return new ServiceResult<ClothesDto> { Success = false, Message = $"Lỗi: {ex.Message}" };
        }
    }

    public async Task<ServiceResult<ClothesDto>> GetByCodeAsync(string code)
    {
        try
        {
            var p = await _dbContext.Products
                .Include(prod => prod.PriceList)
                .FirstOrDefaultAsync(prod => prod.Code.Trim().ToLower() == code.Trim().ToLower());

            if (p == null)
            {
                return new ServiceResult<ClothesDto> { Success = false, Message = "Không tìm thấy sản phẩm với mã barcode này." };
            }

            var categoryName = await _dbContext.Categories
                .Where(c => p.Code.StartsWith(c.CodePrefix))
                .Select(c => c.Name)
                .FirstOrDefaultAsync() ?? "Khác";

            var dto = new ClothesDto
            {
                Id = p.Id,
                Code = p.Code,
                Name = p.Name,
                Description = p.Description ?? string.Empty,
                ImageUrl = p.ImageUrl ?? string.Empty,
                PricePerDay = p.PriceList != null ? p.PriceList.PricePerDay : 0,
                Deposit = p.PriceList != null ? p.PriceList.Deposit : 0,
                Size = p.Size ?? string.Empty,
                Color = p.Color ?? string.Empty,
                StockQuantity = p.StockQuantity,
                IsAvailable = p.IsAvailable && p.StockQuantity > 0,
                CategoryName = categoryName,
                ImportPrice = p.ImportPrice,
                TotalRentRevenue = p.TotalRentRevenue,
                IsLiquidated = p.IsLiquidated
            };

            return new ServiceResult<ClothesDto> { Success = true, Data = dto };
        }
        catch (Exception ex)
        {
            return new ServiceResult<ClothesDto> { Success = false, Message = $"Lỗi quét barcode: {ex.Message}" };
        }
    }

    public async Task<ServiceResult<ClothesDto>> CreateAsync(ClothesDto dto, int categoryId, int priceListId)
    {
        try
        {
            var category = await _dbContext.Categories.FindAsync(categoryId);
            var priceList = await _dbContext.PriceLists.FindAsync(priceListId);

            if (category == null || priceList == null)
            {
                return new ServiceResult<ClothesDto> { Success = false, Message = "Danh mục hoặc bảng giá không hợp lệ." };
            }

            // Tự sinh mã Barcode: [Prefix] + yyyyMMdd + [4 số thứ tự]
            var todayStr = DateTime.Now.ToString("yyyyMMdd");
            var prefix = category.CodePrefix;
            var matchPattern = $"{prefix}{todayStr}%";

            var lastProduct = await _dbContext.Products
                .Where(p => EF.Functions.Like(p.Code, matchPattern))
                .OrderByDescending(p => p.Code)
                .FirstOrDefaultAsync();

            int nextSeq = 1;
            if (lastProduct != null && lastProduct.Code.Length >= 4)
            {
                var seqStr = lastProduct.Code.Substring(lastProduct.Code.Length - 4);
                if (int.TryParse(seqStr, out int lastSeq))
                {
                    nextSeq = lastSeq + 1;
                }
            }

            var generatedCode = $"{prefix}{todayStr}{nextSeq:D4}";

            var product = new Product
            {
                Code = generatedCode,
                Name = dto.Name,
                StockQuantity = dto.StockQuantity,
                Size = dto.Size,
                Color = dto.Color,
                DynamicAttributes = "[]",
                Description = dto.Description,
                ImportPrice = dto.ImportPrice,
                PriceListId = priceList.Id,
                ImageUrl = string.IsNullOrWhiteSpace(dto.ImageUrl) ? "https://images.unsplash.com/photo-1512436991641-6745cdb1723f?q=80&w=600" : dto.ImageUrl,
                IsAvailable = dto.StockQuantity > 0,
                IsLiquidated = false
            };

            _dbContext.Products.Add(product);
            await _dbContext.SaveChangesAsync();

            dto.Id = product.Id;
            dto.Code = product.Code;
            dto.CategoryName = category.Name;
            dto.PricePerDay = priceList.PricePerDay;
            dto.Deposit = priceList.Deposit;

            return new ServiceResult<ClothesDto> { Success = true, Message = "Nhập kho sản phẩm mới thành công.", Data = dto };
        }
        catch (Exception ex)
        {
            return new ServiceResult<ClothesDto> { Success = false, Message = $"Lỗi nhập kho: {ex.Message}" };
        }
    }

    public async Task<ServiceResult> LiquidateAsync(int id, int quantity)
    {
        try
        {
            var p = await _dbContext.Products.FindAsync(id);
            if (p == null)
            {
                return new ServiceResult { Success = false, Message = "Không tìm thấy sản phẩm." };
            }

            if (p.StockQuantity < quantity)
            {
                return new ServiceResult { Success = false, Message = $"Số lượng tồn kho hiện tại ({p.StockQuantity}) không đủ để thanh lý ({quantity})." };
            }

            p.StockQuantity -= quantity;
            if (p.StockQuantity <= 0)
            {
                p.IsAvailable = false;
                p.IsLiquidated = true; // Ngừng sử dụng nếu hết sạch hàng thanh lý
            }

            await _dbContext.SaveChangesAsync();
            return new ServiceResult { Success = true, Message = $"Thanh lý thành công {quantity} sản phẩm." };
        }
        catch (Exception ex)
        {
            return new ServiceResult { Success = false, Message = $"Lỗi thanh lý: {ex.Message}" };
        }
    }

    public async Task<ServiceResult<IEnumerable<Category>>> GetCategoriesAsync()
    {
        var list = await _dbContext.Categories.ToListAsync();
        return new ServiceResult<IEnumerable<Category>> { Success = true, Data = list };
    }

    public async Task<ServiceResult<IEnumerable<PriceList>>> GetPriceListsAsync()
    {
        var list = await _dbContext.PriceLists.ToListAsync();
        return new ServiceResult<IEnumerable<PriceList>> { Success = true, Data = list };
    }
}

