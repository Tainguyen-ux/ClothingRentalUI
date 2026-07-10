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

public class DetailModel : PageModel
{
    private readonly ClothingRentalDbContext _context;
    public DetailModel(ClothingRentalDbContext context) { _context = context; }

    public Order? OrderData { get; set; }
    public List<Transaction> Transactions { get; set; } = new();
    public List<string> CurrentUserPermissions { get; set; } = new();
    public bool IsAdmin { get; set; }

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

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
            .Include(o => o.OrderDetails).ThenInclude(od => od.Product).ThenInclude(p => p!.Category)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (OrderData == null) { ErrorMessage = "Không tìm thấy đơn hàng."; return RedirectToPage("/Orders/Index"); }

        Transactions = await _context.Transactions.Where(t => t.OrderId == id).OrderByDescending(t => t.TransactionDate).ToListAsync();
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
            detail.PenaltyFee = penaltyFee;
            detail.PenaltyReason = penaltyReason;

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
            order.FinalAmount = order.TotalPrice + order.TotalPenalty;

            if (penaltyFee > 0)
            {
                _context.Transactions.Add(new Transaction { OrderId = order.Id, Type = "PENALTY_PAYMENT", PaymentMethod = "CASH", Amount = penaltyFee, PerformedBy = user?.Username ?? "system", TransactionDate = DateTime.UtcNow, Notes = $"Thu phạt: {penaltyReason}" });
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
            order.FinalAmount = order.TotalPrice + order.TotalPenalty;
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
}
