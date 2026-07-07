using Microsoft.AspNetCore.Mvc.RazorPages;
using ClothingRentalUI.Models.Clothes;
using ClothingRentalUI.Services;

namespace ClothingRentalUI.Pages.Clothes;

public class IndexModel : PageModel
{
    private readonly IClothesService _clothesService;

    public IndexModel(IClothesService clothesService)
    {
        _clothesService = clothesService;
    }

    // Danh sách sản phẩm hiển thị trên giao diện
    public IEnumerable<ClothesDto> ClothesList { get; set; } = Enumerable.Empty<ClothesDto>();
    
    // Lưu trữ thông điệp lỗi nếu có
    public string? ErrorMessage { get; set; }
    
    // Trạng thái thành công của yêu cầu
    public bool IsSuccess { get; set; } = true;

    public async Task OnGetAsync(string? category)
    {
        var response = await _clothesService.GetAllAsync(category);
        
        if (response.Success)
        {
            ClothesList = response.Data ?? Enumerable.Empty<ClothesDto>();
            IsSuccess = true;
        }
        else
        {
            ErrorMessage = response.Message ?? "Đã xảy ra lỗi không xác định khi tải danh sách sản phẩm.";
            IsSuccess = false;
            
            // Dữ liệu giả lập để giao diện vẫn hiển thị đẹp mắt khi chưa kết nối API thực tế
            ClothesList = GetMockData();
        }
    }

    private List<ClothesDto> GetMockData()
    {
        return new List<ClothesDto>
        {
            new() {
                Id = 1,
                Name = "Áo dài truyền thống gấm đỏ",
                Description = "Áo dài chất liệu gấm cao cấp thêu họa tiết chim phượng tinh xảo, thích hợp cho lễ hội và đám hỏi.",
                ImageUrl = "https://images.unsplash.com/photo-1621184455862-c163dfb30e0f?q=80&w=600",
                PricePerDay = 250000,
                Size = "M",
                Color = "Đỏ",
                StockQuantity = 5,
                IsAvailable = true,
                CategoryName = "Áo dài"
            },
            new() {
                Id = 2,
                Name = "Vest nam hoàng gia lịch lãm",
                Description = "Bộ vest nam màu xanh Navy kiểu dáng Slim-fit hiện đại phong cách châu Âu quý phái.",
                ImageUrl = "https://images.unsplash.com/photo-1594938298603-c8148c4dae35?q=80&w=600",
                PricePerDay = 350000,
                Size = "L",
                Color = "Xanh Navy",
                StockQuantity = 3,
                IsAvailable = true,
                CategoryName = "Vest"
            },
            new() {
                Id = 3,
                Name = "Váy cưới công chúa trễ vai",
                Description = "Váy cưới phủ kim sa lấp lánh đính đá quý cao cấp nâng dáng cô dâu trong ngày trọng đại.",
                ImageUrl = "https://images.unsplash.com/photo-1594552072238-b8a33785b261?q=80&w=600",
                PricePerDay = 1500000,
                Size = "S",
                Color = "Trắng",
                StockQuantity = 2,
                IsAvailable = true,
                CategoryName = "Váy cưới"
            }
        };
    }
}
