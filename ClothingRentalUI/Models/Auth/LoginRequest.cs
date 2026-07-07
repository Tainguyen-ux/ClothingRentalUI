using System.ComponentModel.DataAnnotations;

namespace ClothingRentalUI.Models.Auth;

public class LoginRequest
{
    [Required(ErrorMessage = "Tên đăng nhập không được để trống")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mật khẩu không được để trống")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}
