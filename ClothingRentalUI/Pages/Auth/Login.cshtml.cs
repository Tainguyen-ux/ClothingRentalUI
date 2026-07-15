using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ClothingRentalUI.Models.Auth;
using ClothingRentalUI.Services;
using ClothingRentalUI.Data;

namespace ClothingRentalUI.Pages.Auth;

public class LoginModel : PageModel
{
    private readonly IAuthService _authService;
    private readonly ClothingRentalDbContext _context;

    public LoginModel(IAuthService authService, ClothingRentalDbContext context)
    {
        _authService = authService;
        _context = context;
    }

    [BindProperty]
    public LoginRequest LoginData { get; set; } = new();

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        // Nếu đã đăng nhập rồi thì redirect thẳng vào trang chủ
        if (!string.IsNullOrEmpty(HttpContext.Session.GetString("Username")))
        {
            return RedirectToPage("/Index");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var response = await _authService.LoginAsync(LoginData);

        if (response.Success && response.Data != null)
        {
            // Lưu thông tin đăng nhập vào Session
            HttpContext.Session.SetString("JWToken", response.Data.Token);
            HttpContext.Session.SetString("Username", response.Data.Username);
            HttpContext.Session.SetString("FullName", response.Data.FullName);
            HttpContext.Session.SetString("Role", response.Data.Role);

            return RedirectToPage("/Index");
        }

        ErrorMessage = response.Message ?? "Đăng nhập thất bại. Vui lòng kiểm tra lại thông tin.";
        return Page();
    }
}
