using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;

namespace ClothingRentalUI.Pages.Orders;

public class CreateModel : PageModel
{
    private readonly ClothingRentalDbContext _context;
    public CreateModel(ClothingRentalDbContext context) { _context = context; }

    public List<Customer> Customers { get; set; } = new();
    [TempData] public string? ErrorMessage { get; set; }

    private async Task<IActionResult?> VerifyAccessAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");
        var user = await _context.Users.Include(u => u.UserPermissions).ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user == null) return RedirectToPage("/Auth/Login");
        bool isAdmin = user.Role == "Admin";
        var perms = user.UserPermissions.Where(up => up.Permission != null).Select(up => up.Permission!.Code).ToList();
        if (!isAdmin && !perms.Contains("ORDER_CREATE")) return RedirectToPage("/Orders/Index");
        return null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;
        Customers = await _context.Customers.Where(c => c.Status == "Active").OrderBy(c => c.FullName).ToListAsync();
        return Page();
    }

    // AJAX: Search available products
    public async Task<IActionResult> OnGetSearchProductsAsync(string term)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false });
        var products = await _context.Products
            .Include(p => p.PriceList).Include(p => p.Category)
            .Where(p => p.IsAvailable && !p.IsLiquidated && p.StockQuantity > p.RentedQuantity
                && (p.Name.ToLower().Contains(term.ToLower()) || p.Code.ToLower().Contains(term.ToLower())))
            .Take(20)
            .Select(p => new {
                p.Id, p.Code, p.Name, p.Size, p.Color,
                categoryName = p.Category != null ? p.Category.Name : "",
                pricePerDay = p.PriceList != null ? p.PriceList.PricePerDay : 0,
                deposit = p.PriceList != null ? p.PriceList.Deposit : 0,
                available = p.StockQuantity - p.RentedQuantity
            })
            .ToListAsync();
        return new JsonResult(new { success = true, data = products });
    }

    // POST: Create order
    public async Task<IActionResult> OnPostCreateOrderAjaxAsync([FromBody] CreateOrderRequest request)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền." });

        var username = HttpContext.Session.GetString("Username") ?? "system";
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (request.CustomerId <= 0 || request.Items == null || !request.Items.Any())
            return new JsonResult(new { success = false, message = "Vui lòng chọn khách hàng và thêm ít nhất 1 sản phẩm." });

        if (request.RentDays < 1)
            return new JsonResult(new { success = false, message = "Số ngày thuê phải lớn hơn 0." });

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Generate code: HD + yyMMdd + 4-digit
            DateTime vnNow = DateTime.UtcNow.AddHours(7);
            string todayStr = vnNow.ToString("yyMMdd");
            var count = await _context.Orders.Where(o => o.Code.StartsWith("HD" + todayStr)).CountAsync();
            string code = $"HD{todayStr}{(count + 1):D4}";

            var rentDate = DateTime.SpecifyKind(vnNow.Date, DateTimeKind.Utc);
            var dueDate = DateTime.SpecifyKind(vnNow.Date.AddDays(request.RentDays), DateTimeKind.Utc);

            var order = new Order
            {
                Code = code,
                CustomerId = request.CustomerId,
                RentDate = rentDate,
                DueDate = dueDate,
                Status = "Draft",
                DepositStatus = "None",
                CreatedByUserId = user?.Id,
                CreatedAt = DateTime.UtcNow,
                Notes = request.Notes
            };

            decimal totalPrice = 0, totalDeposit = 0;

            foreach (var item in request.Items)
            {
                var product = await _context.Products.Include(p => p.PriceList).FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product == null) throw new Exception($"Không tìm thấy sản phẩm ID {item.ProductId}.");
                if (product.StockQuantity <= product.RentedQuantity) throw new Exception($"Sản phẩm '{product.Name}' đã hết hàng.");

                decimal rentPrice = product.PriceList?.PricePerDay ?? 0;
                decimal deposit = product.PriceList?.Deposit ?? 0;
                decimal itemTotal = rentPrice * request.RentDays;

                order.OrderDetails.Add(new OrderDetail
                {
                    ProductId = product.Id,
                    RentPrice = rentPrice,
                    Deposit = deposit,
                    RentDays = request.RentDays
                });

                totalPrice += itemTotal;
                totalDeposit += deposit;
            }

            order.TotalPrice = totalPrice;
            order.TotalDeposit = totalDeposit;
            order.FinalAmount = totalPrice;

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return new JsonResult(new { success = true, orderId = order.Id, code = order.Code });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    public class CreateOrderRequest
    {
        public int CustomerId { get; set; }
        public int RentDays { get; set; } = 1;
        public string? Notes { get; set; }
        public List<OrderItemRequest> Items { get; set; } = new();
    }

    public class OrderItemRequest
    {
        public int ProductId { get; set; }
    }
}
