using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

    public IEnumerable<ClothesDto> ClothesList { get; set; } = Enumerable.Empty<ClothesDto>();
    
    public string? ErrorMessage { get; set; }
    
    public bool IsSuccess { get; set; } = true;

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchQuery { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // 1. Kiểm tra xác thực người dùng qua Session
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToPage("/Auth/Login");
        }

        // 2. Lấy danh sách trang phục
        var response = await _clothesService.GetAllAsync(Category, SearchQuery);
        
        if (response.Success)
        {
            ClothesList = response.Data ?? Enumerable.Empty<ClothesDto>();
            IsSuccess = true;
        }
        else
        {
            ErrorMessage = response.Message ?? "Đã xảy ra lỗi khi tải danh sách trang phục.";
            IsSuccess = false;
        }

        return Page();
    }

    public IActionResult OnGetLogout()
    {
        // Xóa phiên làm việc để đăng xuất
        HttpContext.Session.Clear();
        return RedirectToPage("/Auth/Login");
    }
}
