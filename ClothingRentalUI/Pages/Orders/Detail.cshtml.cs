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

public partial class DetailModel : PageModel
{
    private readonly ClothingRentalDbContext _context;
    public DetailModel(ClothingRentalDbContext context) { _context = context; }

    public Order? OrderData { get; set; }
    public List<Transaction> Transactions { get; set; } = new();
    public List<string> CurrentUserPermissions { get; set; } = new();
    public bool IsAdmin { get; set; }
    public decimal LateFeePerDay { get; set; } = 10000;
    public int LateDayThreshold { get; set; } = 4;
    public Dictionary<string, string> UserDisplayMap { get; set; } = new();

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public string GetUserDisplayName(string username)
    {
        if (string.IsNullOrEmpty(username)) return "—";
        if (username.Equals("system", StringComparison.OrdinalIgnoreCase)) return "Hệ thống";
        if (UserDisplayMap.TryGetValue(username.ToLower(), out var fullName) && !string.IsNullOrEmpty(fullName))
        {
            return fullName;
        }
        return username;
    }

    private async Task<(IActionResult? redirect, User? user)> VerifyAccessAsync(string perm = "ORDER_DETAIL")
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return (RedirectToPage("/Auth/Login"), null);
        var user = await _context.Users.Include(u => u.UserPermissions).ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user == null) return (RedirectToPage("/Auth/Login"), null);
        IsAdmin = user.Role == "Admin";
        CurrentUserPermissions = user.UserPermissions.Where(up => up.Permission != null).Select(up => up.Permission!.Code).ToList();
        if (!IsAdmin && !CurrentUserPermissions.Contains(perm)) return (RedirectToPage("/Orders/Index"), null);
        return (null, user);
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var (redirect, _) = await VerifyAccessAsync();
        if (redirect != null) return redirect;

        OrderData = await _context.Orders
            .Include(o => o.Customer).Include(o => o.CreatedByUser).Include(o => o.ClosedByUser)
            .Include(o => o.Voucher)
            .Include(o => o.OrderDetails).ThenInclude(od => od.Product).ThenInclude(p => p!.Category)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (OrderData == null) { ErrorMessage = "Không tìm thấy đơn hàng."; return RedirectToPage("/Orders/Index"); }

        var lfdStr = await GetSettingValueAsync("Rental_LateFeePerDay", "10000");
        if (decimal.TryParse(lfdStr, out decimal lfd)) LateFeePerDay = lfd;

        var ldtStr = await GetSettingValueAsync("Rental_LateDayThreshold", "4");
        if (int.TryParse(ldtStr, out int ldt)) LateDayThreshold = ldt;

        Transactions = await _context.Transactions.Where(t => t.OrderId == id).OrderByDescending(t => t.TransactionDate).ToListAsync();
        UserDisplayMap = await _context.Users.ToDictionaryAsync(u => u.Username.ToLower(), u => u.FullName);
        return Page();
    }

    // Xác nhận đơn: Draft → Rented
    public async Task<IActionResult> OnPostConfirmAsync(int id, string paymentMethod)
    {
        var (redirect, user) = await VerifyAccessAsync("ORDER_CONFIRM");
        if (redirect != null) return redirect;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var order = await _context.Orders.Include(o => o.OrderDetails).FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) throw new Exception("Không tìm thấy đơn hàng.");
            if (order.Status != "Draft") throw new Exception("Chỉ có thể xác nhận đơn ở trạng thái Nháp.");

            var method = string.IsNullOrEmpty(paymentMethod) ? "CASH" : paymentMethod;

            // Increase RentedQuantity for each product
            foreach (var detail in order.OrderDetails)
            {
                var product = await _context.Products.FindAsync(detail.ProductId);
                if (product == null) throw new Exception($"Không tìm thấy sản phẩm ID {detail.ProductId}.");
                if (product.StockQuantity <= product.RentedQuantity) throw new Exception($"Sản phẩm '{product.Name}' đã hết hàng.");
                product.RentedQuantity += 1;
            }

            order.Status = "Rented";
            order.DepositStatus = "Holding";

            // Record transactions
            _context.Transactions.Add(new Transaction { OrderId = order.Id, Type = "DEPOSIT_RECEIVED", PaymentMethod = method, Amount = order.TotalDeposit, PerformedBy = user?.Username ?? "system", TransactionDate = DateTime.UtcNow, Notes = "Thu tiền cọc khi xác nhận đơn" });
            _context.Transactions.Add(new Transaction { OrderId = order.Id, Type = "RENTAL_PAYMENT", PaymentMethod = method, Amount = order.TotalPrice, PerformedBy = user?.Username ?? "system", TransactionDate = DateTime.UtcNow, Notes = "Thu tiền thuê khi xác nhận đơn" });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            SuccessMessage = "Xác nhận đơn hàng thành công. Đã thu cọc và tiền thuê.";
            TempData["OrderCompletedSpeech"] = "true";
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            ErrorMessage = $"Lỗi: {ex.Message}";
        }
        return RedirectToPage(new { id });
    }

    // Trả hàng: đánh dấu từng sản phẩm IsReturned
    public async Task<IActionResult> OnPostReturnItemAsync(int id, int detailId, decimal penaltyFee, string? penaltyReason)
    {
        var (redirect, user) = await VerifyAccessAsync("ORDER_RETURN");
        if (redirect != null) return redirect;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var order = await _context.Orders.Include(o => o.OrderDetails).FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) throw new Exception("Không tìm thấy đơn hàng.");
            if (order.Status != "Rented" && order.Status != "PartiallyReturned") throw new Exception("Đơn hàng không ở trạng thái cho phép trả hàng.");

            var detail = order.OrderDetails.FirstOrDefault(od => od.Id == detailId);
            if (detail == null) throw new Exception("Không tìm thấy sản phẩm trong đơn.");
            if (detail.IsReturned) throw new Exception("Sản phẩm này đã được trả trước đó.");

            detail.IsReturned = true;
            detail.ReturnDate = DateTime.UtcNow;
            
            if (detail.IsPenaltyPaid)
            {
                if (penaltyFee > 0)
                {
                    detail.PenaltyFee += penaltyFee;
                    detail.PenaltyReason = string.IsNullOrEmpty(detail.PenaltyReason) 
                        ? penaltyReason 
                        : detail.PenaltyReason + "; " + penaltyReason;
                }
            }
            else
            {
                detail.PenaltyFee = penaltyFee;
                detail.PenaltyReason = penaltyReason;
            }

            // Decrease RentedQuantity
            var product = await _context.Products.FindAsync(detail.ProductId);
            if (product != null)
            {
                product.RentedQuantity = Math.Max(0, product.RentedQuantity - 1);
                product.TotalRentRevenue += detail.RentPrice * detail.RentDays;
            }

            // Update order status
            var allReturned = order.OrderDetails.All(od => od.IsReturned);
            var someReturned = order.OrderDetails.Any(od => od.IsReturned);
            order.Status = allReturned ? "Closed" : (someReturned ? "PartiallyReturned" : "Rented");

            // Update penalty totals
            order.TotalPenalty = order.OrderDetails.Sum(od => od.PenaltyFee);
            order.FinalAmount = order.TotalPrice - order.DiscountAmount + order.TotalPenalty;

            if (penaltyFee > 0)
            {
                _context.Transactions.Add(new Transaction { OrderId = order.Id, Type = "PENALTY_PAYMENT", PaymentMethod = "CASH", Amount = penaltyFee, PerformedBy = user?.Username ?? "system", TransactionDate = DateTime.UtcNow, Notes = $"Thu phí phát sinh: {penaltyReason}" });
            }

            if (allReturned)
            {
                order.ActualReturnDate = DateTime.UtcNow;
                order.ClosedByUserId = user?.Id;
                order.DepositStatus = "Refunded";
                _context.Transactions.Add(new Transaction { OrderId = order.Id, Type = "DEPOSIT_REFUNDED", PaymentMethod = "CASH", Amount = order.TotalDeposit, PerformedBy = user?.Username ?? "system", TransactionDate = DateTime.UtcNow, Notes = "Hoàn cọc khi trả hết hàng" });
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            SuccessMessage = allReturned ? "Đã trả hết hàng và đóng đơn thành công." : "Đã xác nhận trả sản phẩm.";
            if (allReturned)
            {
                TempData["OrderCompletedSpeech"] = "true";
            }
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            ErrorMessage = $"Lỗi: {ex.Message}";
        }
        return RedirectToPage(new { id });
    }

    // Gia hạn & Ghi nhận phát sinh thủ công khi khách đang thuê
    public async Task<IActionResult> OnPostUpdateItemPenaltyAsync(int id, int detailId, int extendedDays, decimal penaltyFee, string? penaltyReason, bool collectPaymentNow = false, string? paymentMethod = "CASH")
    {
        var (redirect, user) = await VerifyAccessAsync("ORDER_RETURN");
        if (redirect != null) return redirect;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var order = await _context.Orders.Include(o => o.OrderDetails).FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) throw new Exception("Không tìm thấy đơn hàng.");
            if (order.Status != "Rented" && order.Status != "PartiallyReturned") throw new Exception("Đơn hàng không ở trạng thái cho phép cập nhật gia hạn/phát sinh.");

            var detail = order.OrderDetails.FirstOrDefault(od => od.Id == detailId);
            if (detail == null) throw new Exception("Không tìm thấy sản phẩm trong đơn.");
            if (detail.IsReturned) throw new Exception("Sản phẩm này đã được trả trước đó.");

            detail.ExtendedDays = extendedDays;
            detail.PenaltyFee = penaltyFee;
            detail.PenaltyReason = penaltyReason;

            if (collectPaymentNow)
            {
                detail.IsPenaltyPaid = true;
                if (penaltyFee > 0)
                {
                    _context.Transactions.Add(new Transaction
                    {
                        OrderId = order.Id,
                        Type = "PENALTY_PAYMENT",
                        PaymentMethod = string.IsNullOrEmpty(paymentMethod) ? "CASH" : paymentMethod,
                        Amount = penaltyFee,
                        PerformedBy = user?.Username ?? "system",
                        TransactionDate = DateTime.UtcNow,
                        Notes = $"Thu trước phí phát sinh (Gia hạn/Phụ thu): {penaltyReason}"
                    });
                }
            }
            else
            {
                detail.IsPenaltyPaid = false;
            }

            // Tính toán lại tổng phạt phát sinh
            order.TotalPenalty = order.OrderDetails.Sum(od => od.PenaltyFee);
            order.FinalAmount = order.TotalPrice - order.DiscountAmount + order.TotalPenalty;

            // Gia hạn DueDate dựa trên Max(RentDays + ExtendedDays)
            if (order.OrderDetails.Any())
            {
                int maxTotalDays = order.OrderDetails.Max(od => od.RentDays + od.ExtendedDays);
                order.DueDate = order.RentDate.AddDays(maxTotalDays);
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            SuccessMessage = collectPaymentNow ? "Ghi nhận gia hạn & thu tiền phát sinh thành công." : "Ghi nhận thông tin gia hạn & phát sinh thành công.";
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            ErrorMessage = $"Lỗi: {ex.Message}";
        }
        return RedirectToPage(new { id });
    }

    // Đóng đơn thủ công: force close
    public async Task<IActionResult> OnPostCloseAsync(int id)
    {
        var (redirect, user) = await VerifyAccessAsync("ORDER_CLOSE");
        if (redirect != null) return redirect;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var order = await _context.Orders.Include(o => o.OrderDetails).FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) throw new Exception("Không tìm thấy đơn hàng.");
            if (order.Status == "Closed") throw new Exception("Đơn hàng đã đóng.");

            // Return all unreturned items
            foreach (var detail in order.OrderDetails.Where(od => !od.IsReturned))
            {
                detail.IsReturned = true;
                detail.ReturnDate = DateTime.UtcNow;
                var product = await _context.Products.FindAsync(detail.ProductId);
                if (product != null)
                {
                    product.RentedQuantity = Math.Max(0, product.RentedQuantity - 1);
                    product.TotalRentRevenue += detail.RentPrice * detail.RentDays;
                }
            }

            order.TotalPenalty = order.OrderDetails.Sum(od => od.PenaltyFee);
            order.FinalAmount = order.TotalPrice - order.DiscountAmount + order.TotalPenalty;
            order.Status = "Closed";
            order.ActualReturnDate = DateTime.UtcNow;
            order.ClosedByUserId = user?.Id;
            order.DepositStatus = "Refunded";

            _context.Transactions.Add(new Transaction { OrderId = order.Id, Type = "DEPOSIT_REFUNDED", PaymentMethod = "CASH", Amount = order.TotalDeposit, PerformedBy = user?.Username ?? "system", TransactionDate = DateTime.UtcNow, Notes = "Hoàn cọc khi đóng đơn" });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            SuccessMessage = "Đã đóng đơn hàng và hoàn cọc thành công.";
            TempData["OrderCompletedSpeech"] = "true";
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            ErrorMessage = $"Lỗi: {ex.Message}";
        }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostCancelTransactionAsync(int id, int transactionId)
    {
        var (redirect, user) = await VerifyAccessAsync("TRANSACTION_CANCEL");
        if (redirect != null) return redirect;

        var currentUsername = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(currentUsername))
        {
            return RedirectToPage("/Auth/Login");
        }

        using var dbTransaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var transaction = await _context.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId);
            if (transaction == null) 
                throw new Exception("Không tìm thấy giao dịch.");

            if (transaction.Type.EndsWith("_CANCEL"))
            {
                throw new Exception("Không thể hủy phiếu hủy đối chiếu.");
            }

            // Nếu không phải người thực hiện giao dịch, bắt buộc phải có quyền TRANSACTION_CANCEL_ANY hoặc là Admin
            if (!transaction.PerformedBy.Equals(currentUsername, StringComparison.OrdinalIgnoreCase))
            {
                bool canCancelAny = IsAdmin || CurrentUserPermissions.Contains("TRANSACTION_CANCEL_ANY");
                if (!canCancelAny)
                {
                    throw new Exception("Bạn không có quyền hủy phiếu thu của người khác.");
                }
            }

            var order = await _context.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) 
                throw new Exception("Không tìm thấy đơn hàng tương ứng.");

            // Xử lý hoàn trả/lưu vết ngược tùy theo loại giao dịch
            if (transaction.Type == "DEPOSIT_RECEIVED")
            {
                order.DepositStatus = "None";
                if (order.Status == "Rented")
                {
                    order.Status = "Draft";
                    foreach (var detail in order.OrderDetails)
                    {
                        var product = await _context.Products.FindAsync(detail.ProductId);
                        if (product != null)
                        {
                            product.RentedQuantity = Math.Max(0, product.RentedQuantity - 1);
                        }
                    }
                }
            }
            else if (transaction.Type == "RENTAL_PAYMENT")
            {
                if (order.Status == "Rented")
                {
                    order.Status = "Draft";
                    order.DepositStatus = "None";
                    foreach (var detail in order.OrderDetails)
                    {
                        var product = await _context.Products.FindAsync(detail.ProductId);
                        if (product != null)
                        {
                            product.RentedQuantity = Math.Max(0, product.RentedQuantity - 1);
                        }
                    }
                }
            }
            else if (transaction.Type == "DEPOSIT_REFUNDED")
            {
                order.DepositStatus = "Holding";
                if (order.Status == "Closed")
                {
                    order.Status = "Rented"; // Hoàn lại trạng thái Đang thuê nếu hủy hoàn cọc
                }
            }
            else if (transaction.Type == "PENALTY_PAYMENT")
            {
                foreach (var detail in order.OrderDetails)
                {
                    if (detail.PenaltyFee == transaction.Amount)
                    {
                        detail.PenaltyFee = 0;
                        detail.PenaltyReason = null;
                        break;
                    }
                }
                order.TotalPenalty = order.OrderDetails.Sum(od => od.PenaltyFee);
                order.FinalAmount = order.TotalPrice - order.DiscountAmount + order.TotalPenalty;
            }

            _context.Transactions.Remove(transaction);
            await _context.SaveChangesAsync();
            await dbTransaction.CommitAsync();

            SuccessMessage = "Hủy phiếu thu và hoàn trả trạng thái thành công.";
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync();
            ErrorMessage = $"Lỗi hủy phiếu thu: {ex.Message}";
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostReopenAsync(int id)
    {
        var (redirect, user) = await VerifyAccessAsync("ORDER_REOPEN");
        if (redirect != null) return redirect;

        using var dbTransaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var order = await _context.Orders.Include(o => o.OrderDetails).FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) throw new Exception("Không tìm thấy đơn hàng.");
            if (order.Status != "Closed") throw new Exception("Chỉ có thể mở lại đơn hàng đã đóng.");

            // 1. Revert order status and reset return info
            order.Status = "Rented";
            order.ActualReturnDate = null;
            order.ClosedByUserId = null;
            order.DepositStatus = "Holding";
            order.TotalPenalty = 0;
            order.FinalAmount = order.TotalPrice - order.DiscountAmount;

            // 2. Process order details and update product rented quantities
            foreach (var detail in order.OrderDetails)
            {
                // Reset return state
                detail.IsReturned = false;
                detail.ReturnDate = null;
                detail.PenaltyFee = 0;
                detail.PenaltyReason = null;
                detail.ExtendedDays = 0;

                var product = await _context.Products.FindAsync(detail.ProductId);
                if (product != null)
                {
                    // Increment RentedQuantity
                    product.RentedQuantity += 1;
                    // Deduct the accumulated rent revenue that was added when closed/returned
                    product.TotalRentRevenue = Math.Max(0, product.TotalRentRevenue - detail.RentPrice * detail.RentDays);
                }
            }

            // 3. Create cancellation transactions (phiếu hủy) for all existing transactions of this order
            var existingTransactions = await _context.Transactions.Where(t => t.OrderId == id).ToListAsync();
            foreach (var t in existingTransactions)
            {
                // Only cancel return-phase transaction types: DEPOSIT_REFUNDED or PENALTY_PAYMENT
                if (t.Type != "DEPOSIT_REFUNDED" && t.Type != "PENALTY_PAYMENT") continue;

                var cancelType = t.Type + "_CANCEL";
                // Check if already cancelled to prevent duplicate cancellations
                var alreadyCancelled = existingTransactions.Any(et => et.Type == cancelType);
                if (!alreadyCancelled)
                {
                    var typeName = t.Type switch {
                        "DEPOSIT_RECEIVED" => "Thu tiền cọc",
                        "DEPOSIT_REFUNDED" => "Hoàn cọc",
                        "RENTAL_PAYMENT" => "Thu tiền thuê",
                        "PENALTY_PAYMENT" => "Thu phí phát sinh",
                        _ => t.Type
                    };

                    _context.Transactions.Add(new Transaction
                    {
                        OrderId = order.Id,
                        Type = cancelType,
                        PaymentMethod = t.PaymentMethod,
                        Amount = t.Amount,
                        PerformedBy = user?.Username ?? "system",
                        TransactionDate = DateTime.UtcNow,
                        Notes = $"Hủy: {typeName} (Mở lại đơn hàng #{order.Code})"
                    });
                }
            }

            await _context.SaveChangesAsync();
            await dbTransaction.CommitAsync();
            SuccessMessage = "Đã mở lại đơn hàng thành công. Trạng thái đã chuyển về Đang thuê và các giao dịch cũ đã bị hủy đối chiếu.";
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync();
            ErrorMessage = $"Lỗi: {ex.Message}";
        }
        return RedirectToPage(new { id });
    }

    private class SettingJson
    {
        public string value { get; set; } = string.Empty;
    }

    private async Task<string> GetSettingValueAsync(string key, string defaultValue = "")
    {
        var setting = await _context.SystemSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting == null) return defaultValue;
        try 
        {
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var obj = System.Text.Json.JsonSerializer.Deserialize<SettingJson>(setting.ValueJson, options);
            return obj?.value ?? defaultValue;
        } 
        catch 
        { 
            return defaultValue; 
        }
    }

    public async Task<IActionResult> OnGetPrintAsync(int id, string type)
    {
        var (redirect, _) = await VerifyAccessAsync();
        if (redirect != null) return redirect;

        var order = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.CreatedByUser)
            .Include(o => o.OrderDetails).ThenInclude(od => od.Product).ThenInclude(p => p!.Category)
            .Include(o => o.Transactions)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return Content("Không tìm thấy đơn hàng.");

        var shopName = await GetSettingValueAsync("Shop_Name", "9495Comi");
        var shopAddress = await GetSettingValueAsync("Shop_Address", "123 Đường ABC, Quận XYZ, TP. Hồ Chí Minh");
        var shopPhone = await GetSettingValueAsync("Shop_PhoneNumber", "0901234567");
        var shopNotes = await GetSettingValueAsync("Shop_Notes", "Cảm ơn quý khách đã tin tưởng và ủng hộ!");

        string html;
        if (type == "rental")
        {
            html = BuildRentalPrintHtml(order, shopName, shopAddress, shopPhone, shopNotes);
        }
        else
        {
            string printWidth = await GetSettingValueAsync("Print_InvoiceWidth", "80mm");
            if (string.IsNullOrWhiteSpace(printWidth)) printWidth = "80mm";
            html = BuildInvoicePrintHtml(order, shopName, shopAddress, shopPhone, shopNotes, printWidth);
        }

        return Content(html, "text/html");
    }

    public async Task<IActionResult> OnPostUploadLocalImageAsync(IFormFile file)
    {
        try
        {
            var (redirect, user) = await VerifyAccessAsync("ORDER_VIEW");
            if (redirect != null) return new JsonResult(new { success = false, error = "Không có quyền truy cập." });

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
            var (redirect, user) = await VerifyAccessAsync("ORDER_VIEW");
            if (redirect != null) return new JsonResult(new { success = false, message = "Không có quyền truy cập." });

            if (request == null || request.OrderId <= 0)
                return new JsonResult(new { success = false, message = "Dữ liệu không hợp lệ." });

            var order = await _context.Orders.FindAsync(request.OrderId);
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
}
