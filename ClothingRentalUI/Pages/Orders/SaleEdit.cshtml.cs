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

namespace ClothingRentalUI.Pages.Orders;

public class SaleEditModel : PageModel
{
    private readonly ClothingRentalDbContext _context;
    public SaleEditModel(ClothingRentalDbContext context) { _context = context; }

    [TempData] public string? ErrorMessage { get; set; }
    [TempData] public string? SuccessMessage { get; set; }

    public SaleOrder? SaleOrderData { get; set; }

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

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        SaleOrderData = await _context.SaleOrders
            .Include(o => o.Customer)
            .Include(o => o.Voucher)
            .Include(o => o.SaleOrderDetails).ThenInclude(od => od.Product).ThenInclude(p => p!.PriceList)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (SaleOrderData == null)
        {
            ErrorMessage = "Không tìm thấy đơn mua.";
            return RedirectToPage("/Orders/Index");
        }

        if (SaleOrderData.Status != "Draft")
        {
            ErrorMessage = "Chỉ có thể chỉnh sửa đơn mua ở trạng thái bản nháp.";
            return RedirectToPage("/Orders/SaleDetail", new { id = SaleOrderData.Id });
        }

        return Page();
    }

    // AJAX: Search customers
    public async Task<IActionResult> OnGetSearchCustomersAsync(string term)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false });
        var results = await _context.Customers
            .Where(c => c.Status == "Active" && (c.FullName.ToLower().Contains(term.ToLower()) || c.PhoneNumber.Contains(term)))
            .Take(10)
            .Select(c => new { c.Id, c.FullName, c.PhoneNumber, c.IdentityCard, c.Address })
            .ToListAsync();
        return new JsonResult(new { success = true, data = results });
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
                p.Id, p.Code, p.Name, p.Size, p.Color, p.ImageUrl,
                categoryName = p.Category != null ? p.Category.Name : "",
                pricePerDay = 0,
                deposit = 0,
                available = p.StockQuantity - p.RentedQuantity
            })
            .ToListAsync();
        return new JsonResult(new { success = true, data = products });
    }

    // AJAX: Validate Voucher
    public async Task<IActionResult> OnGetValidateVoucherAsync(string code, decimal totalRent)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền truy cập." });

        if (string.IsNullOrWhiteSpace(code))
            return new JsonResult(new { success = false, message = "Mã voucher không được để trống." });

        var voucher = await _context.Vouchers.FirstOrDefaultAsync(v => v.Code.ToUpper() == code.Trim().ToUpper() && v.IsActive);
        if (voucher == null)
            return new JsonResult(new { success = false, message = "Mã giảm giá không tồn tại hoặc đã bị khóa." });

        var now = DateTime.UtcNow;
        if (now < voucher.StartDate || now > voucher.EndDate)
            return new JsonResult(new { success = false, message = "Mã giảm giá không nằm trong thời gian hiệu lực." });

        if (voucher.MaxUsageCount.HasValue && voucher.UsedCount >= voucher.MaxUsageCount.Value)
            return new JsonResult(new { success = false, message = "Mã giảm giá đã hết lượt sử dụng." });

        if (totalRent < voucher.MinOrderAmount)
            return new JsonResult(new { success = false, message = $"Mã giảm giá chỉ áp dụng cho đơn hàng có giá trị tối thiểu từ {voucher.MinOrderAmount:N0}₫." });

        decimal discountAmount = 0;
        if (voucher.DiscountType == "FIXED")
        {
            discountAmount = voucher.DiscountValue;
        }
        else if (voucher.DiscountType == "PERCENT")
        {
            discountAmount = totalRent * (voucher.DiscountValue / 100m);
            if (voucher.MaxDiscountAmount.HasValue && discountAmount > voucher.MaxDiscountAmount.Value)
            {
                discountAmount = voucher.MaxDiscountAmount.Value;
            }
        }

        discountAmount = Math.Min(discountAmount, totalRent);

        return new JsonResult(new {
            success = true,
            code = voucher.Code,
            name = voucher.Name,
            discountType = voucher.DiscountType,
            discountValue = voucher.DiscountValue,
            maxDiscountAmount = voucher.MaxDiscountAmount,
            minOrderAmount = voucher.MinOrderAmount,
            discountAmount = discountAmount
        });
    }

    // POST: Update sale order
    public async Task<IActionResult> OnPostUpdateOrderAjaxAsync([FromBody] UpdateSaleOrderRequest request)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền." });

        if (request.Items == null || !request.Items.Any())
            return new JsonResult(new { success = false, message = "Vui lòng thêm ít nhất 1 sản phẩm." });

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var saleOrder = await _context.SaleOrders
                .Include(o => o.SaleOrderDetails)
                .Include(o => o.Voucher)
                .FirstOrDefaultAsync(o => o.Id == request.OrderId);

            if (saleOrder == null)
                return new JsonResult(new { success = false, message = "Không tìm thấy đơn hàng." });

            if (saleOrder.Status != "Draft")
                return new JsonResult(new { success = false, message = "Chỉ có thể chỉnh sửa đơn hàng ở trạng thái Nháp." });

            int customerId = request.CustomerId;

            if (customerId <= 0)
            {
                if (string.IsNullOrWhiteSpace(request.NewCustomerName) || string.IsNullOrWhiteSpace(request.NewCustomerPhone))
                    return new JsonResult(new { success = false, message = "Vui lòng nhập Tên và SĐT khách hàng." });

                var phone = request.NewCustomerPhone.Trim();
                var existing = await _context.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == phone);
                if (existing != null)
                {
                    customerId = existing.Id;
                }
                else
                {
                    var newCustomer = new Customer
                    {
                        FullName = request.NewCustomerName.Trim(),
                        PhoneNumber = phone,
                        IdentityCard = request.NewCustomerIdCard?.Trim(),
                        Address = request.NewCustomerAddress?.Trim(),
                        Status = "Active",
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Customers.Add(newCustomer);
                    await _context.SaveChangesAsync();
                    customerId = newCustomer.Id;
                }
            }

            saleOrder.CustomerId = customerId;
            saleOrder.Notes = request.Notes;

            if (request.AttachmentUrls != null && request.AttachmentUrls.Any())
            {
                saleOrder.AttachmentUrl = System.Text.Json.JsonSerializer.Serialize(request.AttachmentUrls);
            }
            else
            {
                saleOrder.AttachmentUrl = null;
            }

            // Remove old details
            _context.SaleOrderDetails.RemoveRange(saleOrder.SaleOrderDetails);

            decimal totalPrice = 0;

            foreach (var item in request.Items)
            {
                var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product == null) throw new Exception($"Không tìm thấy sản phẩm ID {item.ProductId}.");
                int qty = item.Quantity > 0 ? item.Quantity : 1;

                int avail = product.StockQuantity - product.RentedQuantity;
                if (avail < qty) throw new Exception($"Sản phẩm '{product.Name}' chỉ còn {avail} chiếc khả dụng.");

                decimal price = item.Price >= 0 ? item.Price : 0;

                saleOrder.SaleOrderDetails.Add(new SaleOrderDetail
                {
                    ProductId = product.Id,
                    Price = price,
                    Quantity = qty
                });

                totalPrice += price * qty;
            }

            // Handle Voucher updates
            Voucher? oldVoucher = saleOrder.Voucher;
            Voucher? newVoucher = null;
            decimal discountAmount = 0;

            if (!string.IsNullOrWhiteSpace(request.VoucherCode))
            {
                newVoucher = await _context.Vouchers.FirstOrDefaultAsync(v => v.Code.ToUpper() == request.VoucherCode.Trim().ToUpper() && v.IsActive);
                if (newVoucher == null)
                    return new JsonResult(new { success = false, message = "Mã giảm giá không tồn tại hoặc đã bị khóa." });

                var now = DateTime.UtcNow;
                if (now < newVoucher.StartDate || now > newVoucher.EndDate)
                    return new JsonResult(new { success = false, message = "Mã giảm giá không nằm trong thời gian hiệu lực." });

                if (oldVoucher == null || oldVoucher.Id != newVoucher.Id)
                {
                    if (newVoucher.MaxUsageCount.HasValue && newVoucher.UsedCount >= newVoucher.MaxUsageCount.Value)
                        return new JsonResult(new { success = false, message = "Mã giảm giá đã hết lượt sử dụng." });
                }

                if (totalPrice < newVoucher.MinOrderAmount)
                    return new JsonResult(new { success = false, message = $"Mã giảm giá chỉ áp dụng cho đơn hàng có giá trị tối thiểu từ {newVoucher.MinOrderAmount:N0}₫." });

                if (newVoucher.DiscountType == "FIXED")
                {
                    discountAmount = newVoucher.DiscountValue;
                }
                else if (newVoucher.DiscountType == "PERCENT")
                {
                    discountAmount = totalPrice * (newVoucher.DiscountValue / 100m);
                    if (newVoucher.MaxDiscountAmount.HasValue && discountAmount > newVoucher.MaxDiscountAmount.Value)
                    {
                        discountAmount = newVoucher.MaxDiscountAmount.Value;
                    }
                }

                discountAmount = Math.Min(discountAmount, totalPrice);
            }

            // Adjust voucher UsedCount
            if (oldVoucher != null && (newVoucher == null || oldVoucher.Id != newVoucher.Id))
            {
                oldVoucher.UsedCount = Math.Max(0, oldVoucher.UsedCount - 1);
                _context.Vouchers.Update(oldVoucher);
            }

            if (newVoucher != null && (oldVoucher == null || oldVoucher.Id != newVoucher.Id))
            {
                newVoucher.UsedCount += 1;
                _context.Vouchers.Update(newVoucher);
            }

            saleOrder.VoucherId = newVoucher?.Id;
            saleOrder.TotalPrice = totalPrice;
            saleOrder.DiscountAmount = discountAmount;
            saleOrder.FinalAmount = totalPrice - discountAmount;

            _context.SaleOrders.Update(saleOrder);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            SuccessMessage = "Cập nhật đơn hàng thành công.";
            return new JsonResult(new { success = true, orderId = saleOrder.Id, code = saleOrder.Code });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            var msg = ex.InnerException?.Message ?? ex.Message;
            return new JsonResult(new { success = false, message = msg });
        }
    }

    public class UpdateSaleOrderRequest
    {
        public int OrderId { get; set; }
        public int CustomerId { get; set; }
        public string? NewCustomerName { get; set; }
        public string? NewCustomerPhone { get; set; }
        public string? NewCustomerIdCard { get; set; }
        public string? NewCustomerAddress { get; set; }
        public string? Notes { get; set; }
        public string? VoucherCode { get; set; }
        public List<string>? AttachmentUrls { get; set; }
        public List<SaleOrderItemRequest> Items { get; set; } = new();
    }

    public class SaleOrderItemRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; } = 1;
        public decimal Price { get; set; }
    }
}
