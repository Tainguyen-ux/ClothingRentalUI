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

public class CreateModel : PageModel
{
    private readonly ClothingRentalDbContext _context;
    public CreateModel(ClothingRentalDbContext context) { _context = context; }

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
        return Page();
    }

    // AJAX: Search customers by name or phone
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
                pricePerDay = p.PriceList != null ? p.PriceList.PricePerDay : 0,
                deposit = p.PriceList != null ? p.PriceList.Deposit : 0,
                available = p.StockQuantity - p.RentedQuantity,
                giftProductsJson = p.PriceList != null ? p.PriceList.GiftProductsJson : "[]"
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
            return new JsonResult(new { success = false, message = $"Mã giảm giá chỉ áp dụng cho đơn thuê có giá trị tối thiểu từ {voucher.MinOrderAmount:N0}₫." });

        // Calculate discount
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

    // POST: Create order (supports inline new customer + quantity per item)
    public async Task<IActionResult> OnPostCreateOrderAjaxAsync([FromBody] CreateOrderRequest request)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền." });

        var username = HttpContext.Session.GetString("Username") ?? "system";
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (request.Items == null || !request.Items.Any())
            return new JsonResult(new { success = false, message = "Vui lòng thêm ít nhất 1 sản phẩm." });
        if (request.RentDays < 1)
            return new JsonResult(new { success = false, message = "Số ngày thuê phải lớn hơn 0." });

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            int customerId = request.CustomerId;

            // Nếu CustomerId == 0, tạo khách hàng mới inline
            if (customerId <= 0)
            {
                if (string.IsNullOrWhiteSpace(request.NewCustomerName) || string.IsNullOrWhiteSpace(request.NewCustomerPhone))
                    return new JsonResult(new { success = false, message = "Vui lòng nhập Tên và SĐT khách hàng." });

                var phone = request.NewCustomerPhone.Trim();
                var existing = await _context.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == phone);
                if (existing != null)
                {
                    customerId = existing.Id;
                    if (request.IsIdCardReceived && !string.IsNullOrWhiteSpace(request.NewCustomerIdCard) && existing.IdentityCard != request.NewCustomerIdCard.Trim())
                    {
                        existing.IdentityCard = request.NewCustomerIdCard.Trim();
                        _context.Customers.Update(existing);
                        await _context.SaveChangesAsync();
                    }
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
            else
            {
                if (request.IsIdCardReceived && !string.IsNullOrWhiteSpace(request.NewCustomerIdCard))
                {
                    var existing = await _context.Customers.FindAsync(customerId);
                    if (existing != null && existing.IdentityCard != request.NewCustomerIdCard.Trim())
                    {
                        existing.IdentityCard = request.NewCustomerIdCard.Trim();
                        _context.Customers.Update(existing);
                        await _context.SaveChangesAsync();
                    }
                }
            }

            // Generate code: HD + yyMMdd + 4-digit
            DateTime vnNow = DateTime.UtcNow.AddHours(7);
            string todayStr = vnNow.ToString("yyMMdd");
            var count = await _context.Orders.Where(o => o.Code.StartsWith("HD" + todayStr)).CountAsync();
            string code = $"HD{todayStr}{(count + 1):D4}";

            var rentDate = DateTime.UtcNow;
            
            // Calculate due date based on the maximum rent days among all selected items
            int maxRentDays = request.Items.Max(i => i.RentDays > 0 ? i.RentDays : 1);
            var dueDate = DateTime.SpecifyKind(vnNow.Date.AddDays(maxRentDays), DateTimeKind.Utc);

            var order = new Order
            {
                Code = code,
                CustomerId = customerId,
                RentDate = rentDate,
                DueDate = dueDate,
                Status = "Draft",
                DepositStatus = "None",
                CreatedByUserId = user?.Id,
                CreatedAt = DateTime.UtcNow,
                Notes = request.Notes,
                IsIdCardReceived = request.IsIdCardReceived,
                AttachmentUrl = request.AttachmentUrls != null && request.AttachmentUrls.Any() 
                    ? System.Text.Json.JsonSerializer.Serialize(request.AttachmentUrls) 
                    : null
            };

            decimal totalPrice = 0, totalDeposit = 0;

            foreach (var item in request.Items)
            {
                var product = await _context.Products.Include(p => p.PriceList).FirstOrDefaultAsync(p => p.Id == item.ProductId);
                if (product == null) throw new Exception($"Không tìm thấy sản phẩm ID {item.ProductId}.");
                int qty = item.Quantity > 0 ? item.Quantity : 1;
                int avail = product.StockQuantity - product.RentedQuantity;
                if (avail < qty) throw new Exception($"Sản phẩm '{product.Name}' chỉ còn {avail} chiếc khả dụng.");

                decimal rentPrice = item.RentPrice >= 0 ? item.RentPrice : (product.PriceList?.PricePerDay ?? 0);
                decimal deposit = item.Deposit >= 0 ? item.Deposit : (product.PriceList?.Deposit ?? 0);
                int itemRentDays = item.RentDays > 0 ? item.RentDays : 1;

                for (int i = 0; i < qty; i++)
                {
                    string? cond = (item.Conditions != null && item.Conditions.Count > i) ? item.Conditions[i] : null;
                    order.OrderDetails.Add(new OrderDetail
                    {
                        ProductId = product.Id,
                        RentPrice = rentPrice,
                        Deposit = deposit,
                        RentDays = itemRentDays,
                        IsGift = item.IsGift,
                        ParentProductId = item.ParentProductId,
                        ConditionAtReceive = cond,
                        PricePerDay = item.PricePerDay,
                        AddAmt = item.AddAmt / qty,
                        DeductAmt = item.DeductAmt / qty
                    });
                }



                totalPrice += rentPrice * qty;
                totalDeposit += deposit * qty;
            }

            Voucher? voucher = null;
            decimal discountAmount = 0;

            if (!string.IsNullOrWhiteSpace(request.VoucherCode))
            {
                voucher = await _context.Vouchers.FirstOrDefaultAsync(v => v.Code.ToUpper() == request.VoucherCode.Trim().ToUpper() && v.IsActive);
                if (voucher == null)
                    return new JsonResult(new { success = false, message = "Mã giảm giá không tồn tại hoặc đã bị khóa." });

                var now = DateTime.UtcNow;
                if (now < voucher.StartDate || now > voucher.EndDate)
                    return new JsonResult(new { success = false, message = "Mã giảm giá không nằm trong thời gian hiệu lực." });

                if (voucher.MaxUsageCount.HasValue && voucher.UsedCount >= voucher.MaxUsageCount.Value)
                    return new JsonResult(new { success = false, message = "Mã giảm giá đã hết lượt sử dụng." });

                if (totalPrice < voucher.MinOrderAmount)
                    return new JsonResult(new { success = false, message = $"Mã giảm giá chỉ áp dụng cho đơn thuê có giá trị tối thiểu từ {voucher.MinOrderAmount:N0}₫." });

                // Calculate discount
                if (voucher.DiscountType == "FIXED")
                {
                    discountAmount = voucher.DiscountValue;
                }
                else if (voucher.DiscountType == "PERCENT")
                {
                    discountAmount = totalPrice * (voucher.DiscountValue / 100m);
                    if (voucher.MaxDiscountAmount.HasValue && discountAmount > voucher.MaxDiscountAmount.Value)
                    {
                        discountAmount = voucher.MaxDiscountAmount.Value;
                    }
                }

                discountAmount = Math.Min(discountAmount, totalPrice);
                
                // Increment voucher usage
                voucher.UsedCount += 1;
                _context.Vouchers.Update(voucher);
            }

            order.TotalPrice = totalPrice;
            order.TotalDeposit = totalDeposit;
            order.DiscountAmount = discountAmount;
            order.FinalAmount = totalPrice - discountAmount;
            if (voucher != null)
            {
                order.VoucherId = voucher.Id;
            }

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return new JsonResult(new { success = true, orderId = order.Id, code = order.Code });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            var msg = ex.InnerException?.Message ?? ex.Message;
            return new JsonResult(new { success = false, message = msg });
        }
    }

    public async Task<IActionResult> OnPostUploadLocalImageAsync(IFormFile file)
    {
        try
        {
            var authCheck = await VerifyAccessAsync();
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

    public class CreateOrderRequest
    {
        public int CustomerId { get; set; }
        public string? NewCustomerName { get; set; }
        public string? NewCustomerPhone { get; set; }
        public string? NewCustomerIdCard { get; set; }
        public string? NewCustomerAddress { get; set; }
        public int RentDays { get; set; } = 1;
        public string? Notes { get; set; }
        public bool IsIdCardReceived { get; set; }
        public string? VoucherCode { get; set; }
        public List<string>? AttachmentUrls { get; set; }
        public List<OrderItemRequest> Items { get; set; } = new();
    }

    public class OrderItemRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; } = 1;
        public int RentDays { get; set; } = 1;
        public decimal RentPrice { get; set; }
        public decimal Deposit { get; set; }
        public bool IsGift { get; set; }
        public int? ParentProductId { get; set; }
        public List<string>? Conditions { get; set; }
        public decimal PricePerDay { get; set; }
        public decimal AddAmt { get; set; }
        public decimal DeductAmt { get; set; }
    }
}


