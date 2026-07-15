using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;
using MiniExcelLibs;

namespace ClothingRentalUI.Pages.Orders;

public class SaleIndexModel : PageModel
{
    private readonly ClothingRentalDbContext _context;
    public SaleIndexModel(ClothingRentalDbContext context) { _context = context; }

    public List<SaleOrder> SaleOrders { get; set; } = new();
    
    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? StatusFilter { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;

    public int PageSize { get; set; } = 10;
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public bool IsAdmin { get; set; }
    public List<string> CurrentUserPermissions { get; set; } = new();

    private async Task<IActionResult?> VerifyAccessAsync(string? requiredPerm = null)
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");
        
        var user = await _context.Users.Include(u => u.UserPermissions).ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user == null) return RedirectToPage("/Auth/Login");

        IsAdmin = user.Role == "Admin";
        CurrentUserPermissions = user.UserPermissions.Where(up => up.Permission != null).Select(up => up.Permission!.Code).ToList();

        if (requiredPerm != null && !IsAdmin && !CurrentUserPermissions.Contains(requiredPerm))
        {
            return RedirectToPage("/Orders/SaleIndex");
        }

        if (!IsAdmin && !CurrentUserPermissions.Contains("ORDER_VIEW"))
        {
            return RedirectToPage("/Index");
        }

        return null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var query = _context.SaleOrders
            .Include(o => o.Customer)
            .Include(o => o.CreatedByUser)
            .Include(o => o.SaleOrderDetails)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(StatusFilter))
            query = query.Where(o => o.Status == StatusFilter);

        if (FromDate.HasValue)
        {
            var startUtc = DateTime.SpecifyKind(FromDate.Value.Date.AddHours(-7), DateTimeKind.Utc);
            query = query.Where(o => o.CreatedAt >= startUtc);
        }

        if (ToDate.HasValue)
        {
            var endUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc);
            query = query.Where(o => o.CreatedAt < endUtc);
        }

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var s = SearchTerm.Trim().ToLower();
            query = query.Where(o => o.Code.ToLower().Contains(s)
                || (o.Customer != null && o.Customer.FullName.ToLower().Contains(s))
                || (o.Customer != null && o.Customer.PhoneNumber.Contains(s)));
        }

        TotalItems = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        SaleOrders = await query.OrderByDescending(o => o.Id).Skip((PageIndex - 1) * PageSize).Take(PageSize).ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnGetExportExcelAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var query = _context.SaleOrders
            .Include(o => o.Customer)
            .Include(o => o.CreatedByUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(StatusFilter))
            query = query.Where(o => o.Status == StatusFilter);

        if (FromDate.HasValue)
        {
            var startUtc = DateTime.SpecifyKind(FromDate.Value.Date.AddHours(-7), DateTimeKind.Utc);
            query = query.Where(o => o.CreatedAt >= startUtc);
        }

        if (ToDate.HasValue)
        {
            var endUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc);
            query = query.Where(o => o.CreatedAt < endUtc);
        }

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var s = SearchTerm.Trim().ToLower();
            query = query.Where(o => o.Code.ToLower().Contains(s)
                || (o.Customer != null && o.Customer.FullName.ToLower().Contains(s))
                || (o.Customer != null && o.Customer.PhoneNumber.Contains(s)));
        }

        var list = await query.OrderByDescending(o => o.Id).ToListAsync();

        var excelData = list.Select((o, index) => {
            string statusStr = o.Status switch {
                "Draft" => "Nháp",
                "Closed" => "Đã đóng",
                "Cancelled" => "Đã hủy",
                _ => o.Status
            };

            return new Dictionary<string, object> {
                { "STT", index + 1 },
                { "Mã đơn", o.Code },
                { "Ngày tạo", o.CreatedAt.AddHours(7).ToString("dd/MM/yyyy HH:mm") },
                { "Khách hàng", o.Customer?.FullName ?? "" },
                { "Điện thoại", o.Customer?.PhoneNumber ?? "" },
                { "Tên nhân viên", o.CreatedByUser?.FullName ?? "" },
                { "Tổng tiền hàng (đ)", o.TotalPrice },
                { "Giảm giá (đ)", o.DiscountAmount },
                { "Thực thu (đ)", o.FinalAmount },
                { "Trạng thái", statusStr }
            };
        }).ToList();

        var memoryStream = new MemoryStream();
        memoryStream.SaveAs(excelData);
        memoryStream.Seek(0, SeekOrigin.Begin);

        var fileName = $"DanhSachDonMua_{DateTime.UtcNow.AddHours(7):yyyyMMdd_HHmmss}.xlsx";
        return File(memoryStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var authCheck = await VerifyAccessAsync("ORDER_DELETE");
        if (authCheck != null) return authCheck;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var order = await _context.SaleOrders
                .Include(o => o.SaleOrderDetails)
                .Include(o => o.Voucher)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                ErrorMessage = "Không tìm thấy đơn hàng.";
                return RedirectToPage();
            }

            if (order.Status != "Draft")
            {
                ErrorMessage = "Chỉ có thể xóa đơn hàng ở trạng thái Nháp.";
                return RedirectToPage();
            }

            if (order.Voucher != null)
            {
                order.Voucher.UsedCount = Math.Max(0, order.Voucher.UsedCount - 1);
                _context.Vouchers.Update(order.Voucher);
            }

            _context.SaleOrders.Remove(order);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            SuccessMessage = $"Đã xóa đơn hàng {order.Code}.";
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            ErrorMessage = $"Lỗi khi xóa đơn hàng: {ex.Message}";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUploadLocalImageAsync(IFormFile file)
    {
        try
        {
            var authCheck = await VerifyAccessAsync("ORDER_VIEW");
            if (authCheck != null) return new JsonResult(new { success = false, error = "Không có quyền truy cập." });

            if (file == null || file.Length == 0)
            {
                return new JsonResult(new { success = false, error = "Tệp tin không hợp lệ." });
            }

            var ext = Path.GetExtension(file.FileName).ToLower();

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }

            var uniqueFileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            var relativeUrl = $"/uploads/{uniqueFileName}";
            return new JsonResult(new { success = true, url = relativeUrl });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = $"Lỗi khi lưu tệp tin: {ex.Message}" });
        }
    }

    public class UpdateAttachmentRequest
    {
        public int OrderId { get; set; }
        public string Url { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostUpdateAttachmentAjaxAsync([FromBody] UpdateAttachmentRequest request)
    {
        try
        {
            var authCheck = await VerifyAccessAsync("ORDER_VIEW");
            if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền truy cập." });

            if (request == null || request.OrderId <= 0)
                return new JsonResult(new { success = false, message = "Dữ liệu không hợp lệ." });

            var order = await _context.SaleOrders.FindAsync(request.OrderId);
            if (order == null)
                return new JsonResult(new { success = false, message = "Không tìm thấy đơn hàng." });

            order.AttachmentUrl = request.Url;
            await _context.SaveChangesAsync();

            return new JsonResult(new { success = true, message = "Cập nhật hình ảnh đính kèm thành công." });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = $"Lỗi máy chủ: {ex.Message}" });
        }
    }

    public async Task<IActionResult> OnGetFindOrderByCodeAsync(string code)
    {
        try
        {
            var authCheck = await VerifyAccessAsync("ORDER_VIEW");
            if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền truy cập." });

            if (string.IsNullOrWhiteSpace(code))
                return new JsonResult(new { success = false, message = "Mã đơn hàng không hợp lệ." });

            var order = await _context.SaleOrders
                .FirstOrDefaultAsync(o => o.Code.ToLower() == code.Trim().ToLower());

            if (order == null)
                return new JsonResult(new { success = false, message = "Không tìm thấy đơn hàng tương ứng." });

            return new JsonResult(new { success = true, id = order.Id });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = $"Lỗi máy chủ: {ex.Message}" });
        }
    }
}
