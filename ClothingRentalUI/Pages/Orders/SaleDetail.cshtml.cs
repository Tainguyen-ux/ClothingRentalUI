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

public class SaleDetailModel : PageModel
{
    private readonly ClothingRentalDbContext _context;
    public SaleDetailModel(ClothingRentalDbContext context) { _context = context; }

    public SaleOrder? SaleOrderData { get; set; }
    public List<Transaction> Transactions { get; set; } = new();
    public Dictionary<string, string> UserDisplayMap { get; set; } = new();

    [TempData] public string? SuccessMessage { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public bool IsAdmin { get; set; }
    public List<string> CurrentUserPermissions { get; set; } = new();

    public string GetUserFullName(string username)
    {
        if (string.IsNullOrEmpty(username)) return "system";
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
        if (!IsAdmin && !CurrentUserPermissions.Contains(perm)) return (RedirectToPage("/Orders/SaleIndex"), null);
        return (null, user);
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var (redirect, _) = await VerifyAccessAsync();
        if (redirect != null) return redirect;

        SaleOrderData = await _context.SaleOrders
            .Include(o => o.Customer).Include(o => o.CreatedByUser)
            .Include(o => o.Voucher)
            .Include(o => o.SaleOrderDetails).ThenInclude(od => od.Product).ThenInclude(p => p!.Category)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (SaleOrderData == null) { ErrorMessage = "Không tìm thấy đơn mua."; return RedirectToPage("/Orders/SaleIndex"); }

        Transactions = await _context.Transactions.Where(t => t.SaleOrderId == id).OrderByDescending(t => t.TransactionDate).ToListAsync();
        UserDisplayMap = await _context.Users.ToDictionaryAsync(u => u.Username.ToLower(), u => u.FullName);
        return Page();
    }

    // Xác nhận đơn: Draft -> Closed, trừ kho trực tiếp
    public async Task<IActionResult> OnPostConfirmAsync(int id, string paymentMethod)
    {
        var (redirect, user) = await VerifyAccessAsync("ORDER_CONFIRM");
        if (redirect != null) return redirect;

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var order = await _context.SaleOrders.Include(o => o.SaleOrderDetails).FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) throw new Exception("Không tìm thấy đơn mua.");
            if (order.Status != "Draft") throw new Exception("Chỉ có thể xác nhận đơn ở trạng thái Nháp.");

            var method = string.IsNullOrEmpty(paymentMethod) ? "CASH" : paymentMethod;

            // Trừ số lượng tồn kho (StockQuantity)
            foreach (var detail in order.SaleOrderDetails)
            {
                var product = await _context.Products.FindAsync(detail.ProductId);
                if (product == null) throw new Exception($"Không tìm thấy sản phẩm ID {detail.ProductId}.");
                int avail = product.StockQuantity - product.RentedQuantity;
                if (avail < detail.Quantity) throw new Exception($"Sản phẩm '{product.Name}' không đủ số lượng để xuất kho.");
                
                product.StockQuantity -= detail.Quantity;
                
                // Lưu lịch sử xuất kho
                _context.StockHistories.Add(new StockHistory
                {
                    ProductId = product.Id,
                    ActionType = "SALE",
                    QuantityChange = -detail.Quantity,
                    PerformedBy = user?.Username ?? "system",
                    Note = $"Xuất kho bán hàng trực tiếp (Đơn mua {order.Code})",
                    CreatedAt = DateTime.UtcNow
                });
            }

            order.Status = "Closed";

            // Ghi nhận giao dịch thanh toán
            _context.Transactions.Add(new Transaction 
            { 
                SaleOrderId = order.Id, 
                Type = "SALE_PAYMENT", 
                PaymentMethod = method, 
                Amount = order.FinalAmount, 
                PerformedBy = user?.Username ?? "system", 
                TransactionDate = DateTime.UtcNow, 
                Notes = "Thu tiền bán hàng trực tiếp khi xác nhận đơn" 
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            SuccessMessage = "Xác nhận đơn mua thành công. Đã xuất kho sản phẩm và thu tiền.";
            TempData["OrderCompletedSpeech"] = "true";
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            ErrorMessage = $"Lỗi: {ex.Message}";
        }
        return RedirectToPage(new { id });
    }

    // Hủy giao dịch lẻ tẻ thủ công
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

            if (!transaction.PerformedBy.Equals(currentUsername, StringComparison.OrdinalIgnoreCase))
            {
                bool canCancelAny = IsAdmin || CurrentUserPermissions.Contains("TRANSACTION_CANCEL_ANY");
                if (!canCancelAny)
                {
                    throw new Exception("Bạn không có quyền hủy phiếu thu của người khác.");
                }
            }

            var order = await _context.SaleOrders
                .Include(o => o.SaleOrderDetails)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) 
                throw new Exception("Không tìm thấy đơn mua tương ứng.");

            _context.Transactions.Remove(transaction);
            await _context.SaveChangesAsync();
            await dbTransaction.CommitAsync();

            SuccessMessage = "Hủy phiếu thu thành công.";
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync();
            ErrorMessage = $"Lỗi hủy phiếu thu: {ex.Message}";
        }

        return RedirectToPage(new { id });
    }

    // Mở lại đơn mua: Closed -> Draft, hoàn lại kho, tạo phiếu hủy cho tất cả giao dịch
    public async Task<IActionResult> OnPostReopenAsync(int id)
    {
        var (redirect, user) = await VerifyAccessAsync("ORDER_REOPEN");
        if (redirect != null) return redirect;

        using var dbTransaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var order = await _context.SaleOrders.Include(o => o.SaleOrderDetails).FirstOrDefaultAsync(o => o.Id == id);
            if (order == null) throw new Exception("Không tìm thấy đơn mua.");
            if (order.Status != "Closed") throw new Exception("Chỉ có thể mở lại đơn mua đã đóng.");

            order.Status = "Draft";

            // Hoàn trả lại số lượng tồn kho (StockQuantity)
            foreach (var detail in order.SaleOrderDetails)
            {
                var product = await _context.Products.FindAsync(detail.ProductId);
                if (product != null)
                {
                    product.StockQuantity += detail.Quantity;
                    
                    // Lưu lịch sử nhập kho
                    _context.StockHistories.Add(new StockHistory
                    {
                        ProductId = product.Id,
                        ActionType = "IMPORT",
                        QuantityChange = detail.Quantity,
                        PerformedBy = user?.Username ?? "system",
                        Note = $"Nhập lại kho khi mở lại đơn mua {order.Code}",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            // Tạo phiếu hủy cho tất cả giao dịch thu tiền của đơn hàng này
            var existingTransactions = await _context.Transactions.Where(t => t.SaleOrderId == id).ToListAsync();
            foreach (var t in existingTransactions)
            {
                if (t.Type.EndsWith("_CANCEL")) continue;

                var cancelType = t.Type + "_CANCEL";
                var alreadyCancelled = existingTransactions.Any(et => et.Type == cancelType);
                if (!alreadyCancelled)
                {
                    var typeName = t.Type switch {
                        "SALE_PAYMENT" => "Thu tiền bán hàng",
                        "RENTAL_PAYMENT" => "Thu tiền bán hàng",
                        _ => t.Type
                    };

                    _context.Transactions.Add(new Transaction
                    {
                        SaleOrderId = order.Id,
                        Type = cancelType,
                        PaymentMethod = t.PaymentMethod,
                        Amount = t.Amount,
                        PerformedBy = user?.Username ?? "system",
                        TransactionDate = DateTime.UtcNow,
                        Notes = $"Hủy: {typeName} (Mở lại đơn mua #{order.Code})"
                    });
                }
            }

            await _context.SaveChangesAsync();
            await dbTransaction.CommitAsync();
            SuccessMessage = "Đã mở lại đơn mua thành công. Trạng thái chuyển về Nháp, tồn kho đã được hoàn lại và tất cả giao dịch cũ đã bị hủy đối chiếu.";
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync();
            ErrorMessage = $"Lỗi khi mở lại đơn: {ex.Message}";
        }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetPrintAsync(int id)
    {
        var (redirect, _) = await VerifyAccessAsync();
        if (redirect != null) return redirect;

        var order = await _context.SaleOrders
            .Include(o => o.Customer).Include(o => o.CreatedByUser)
            .Include(o => o.SaleOrderDetails).ThenInclude(od => od.Product)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) return Content("Không tìm thấy đơn mua.");

        var shopName = await GetSettingValueAsync("Shop_Name", "9495Comi");
        var shopAddress = await GetSettingValueAsync("Shop_Address", "123 Đường ABC, Quận XYZ, TP. Hồ Chí Minh");
        var shopPhone = await GetSettingValueAsync("Shop_PhoneNumber", "0901234567");
        var shopNotes = await GetSettingValueAsync("Shop_Notes", "Cảm ơn quý khách đã tin tưởng và ủng hộ!");

        string title = "HÓA ĐƠN BÁN HÀNG";
        string printWidth = await GetSettingValueAsync("Print_InvoiceWidth", "80mm");
        if (string.IsNullOrWhiteSpace(printWidth)) printWidth = "80mm";

        // Generate HTML
        var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
    <title>{title} - {order.Code}</title>
    <script src=""https://cdn.jsdelivr.net/npm/jsbarcode@3.11.5/dist/JsBarcode.all.min.js""></script>
    <style>
        @page {{
            size: auto;
            margin: 0mm;
        }}
        body {{
            font-family: 'Segoe UI', Arial, sans-serif;
            font-size: 12px;
            color: #333;
            margin: 0;
            padding: 10px;
            width: {printWidth};
            box-sizing: border-box;
        }}
        .header {{
            text-align: center;
            margin-bottom: 15px;
            border-bottom: 1px dashed #ccc;
            padding-bottom: 10px;
        }}
        .shop-name {{
            font-size: 14px;
            font-weight: bold;
            text-transform: uppercase;
            margin-bottom: 4px;
        }}
        .shop-info {{
            font-size: 10px;
            color: #555;
            margin-bottom: 2px;
        }}
        .title {{
            font-size: 14px;
            font-weight: bold;
            margin: 15px 0 5px 0;
            text-align: center;
        }}
        .order-code {{
            font-size: 12px;
            font-weight: bold;
            text-align: center;
            margin-bottom: 15px;
            font-family: monospace;
        }}
        .info-table {{
            width: 100%;
            margin-bottom: 15px;
            font-size: 11px;
        }}
        .info-table td {{
            padding: 2px 0;
            vertical-align: top;
        }}
        .info-label {{
            color: #666;
            width: 85px;
        }}
        .items-table {{
            width: 100%;
            border-collapse: collapse;
            margin-bottom: 15px;
            font-size: 11px;
        }}
        .items-table th {{
            border-bottom: 1px solid #000;
            border-top: 1px solid #000;
            text-align: left;
            padding: 6px 2px;
            font-weight: bold;
        }}
        .items-table td {{
            padding: 6px 2px;
            border-bottom: 1px dashed #eee;
        }}
        .summary-section {{
            border-top: 1px solid #000;
            padding-top: 8px;
            margin-bottom: 15px;
            font-size: 11px;
        }}
        .summary-row {{
            display: flex;
            justify-content: space-between;
            margin-bottom: 4px;
        }}
        .summary-row.total {{
            font-weight: bold;
            font-size: 12px;
            border-top: 1px dashed #ccc;
            padding-top: 4px;
            margin-top: 4px;
        }}
        .footer {{
            text-align: center;
            font-size: 10px;
            color: #555;
            margin-top: 15px;
            border-top: 1px dashed #ccc;
            padding-top: 10px;
        }}
        @media print {{
            body {{
                width: 100%;
                padding: 0;
            }}
        }}
    </style>
</head>
<body>
    <div class=""header"">
        <div class=""shop-name"">{shopName}</div>
        <div class=""shop-info"">📍 {shopAddress}</div>
        <div class=""shop-info"">📞 {shopPhone}</div>
    </div>
    
    <div class=""title"">{title}</div>
    <div class=""order-code"">Mã đơn: {order.Code}</div>
    <div style=""text-align: center; margin: 5px 0 15px 0;""><svg id=""order-barcode""></svg></div>
    
    <table class=""info-table"">
        <tr>
            <td class=""info-label"">Khách hàng:</td>
            <td><strong>{order.Customer?.FullName ?? "Khách lẻ"}</strong></td>
        </tr>
        <tr>
            <td class=""info-label"">Số điện thoại:</td>
            <td>{order.Customer?.PhoneNumber ?? "—"}</td>
        </tr>
        <tr>
            <td class=""info-label"">Ngày mua hàng:</td>
            <td>{order.SaleDate.AddHours(7).ToString("dd/MM/yyyy HH:mm")}</td>
        </tr>
    </table>
    
    <table class=""items-table"">
        <thead>
            <tr>
                <th>Sản phẩm</th>
                <th style=""text-align: right;"">SL</th>
                <th style=""text-align: right;"">Đ.Giá</th>
                <th style=""text-align: right;"">T.Tiền</th>
            </tr>
        </thead>
        <tbody>";

        foreach (var detail in order.SaleOrderDetails)
        {
            var prodName = detail.Product?.Name ?? "Sản phẩm";
            var sizeColor = $"({detail.Product?.Size ?? "—"}/{detail.Product?.Color ?? "—"})";
            var detailTotal = (detail.Price * detail.Quantity).ToString("N0") + "₫";
            
            html += $@"
            <tr>
                <td>
                    <div>{prodName}</div>
                    <div style=""font-size: 9px; color: #666;"">{sizeColor}</div>
                </td>
                <td style=""text-align: right;"">{detail.Quantity}</td>
                <td style=""text-align: right;"">{detail.Price.ToString("N0")}₫</td>
                <td style=""text-align: right; font-weight: bold;"">{detailTotal}</td>
            </tr>";
        }

        html += $@"
        </tbody>
    </table>
    
    <div class=""summary-section"">
        <div class=""summary-row"">
            <span>Tổng tiền hàng:</span>
            <span>{order.TotalPrice.ToString("N0")}₫</span>
        </div>";

        if (order.DiscountAmount > 0)
        {
            html += $@"
        <div class=""summary-row"" style=""color: #2563eb;"">
            <span>Giảm giá (Voucher):</span>
            <span>-{order.DiscountAmount.ToString("N0")}₫</span>
        </div>";
        }

        html += $@"
        <div class=""summary-row total"">
            <span>TỔNG CỘNG KHÁCH TRẢ:</span>
            <span>{order.FinalAmount.ToString("N0")}₫</span>
        </div>
    </div>";

        html += $@"
    <div class=""footer"">
        <div>{shopNotes}</div>
        <div style=""font-size: 9px; margin-top: 5px; color: #888;"">In lúc: {DateTime.UtcNow.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss")}</div>
    </div>
    
    <script>
        window.onload = function() {{
            try {{
                JsBarcode(""#order-barcode"", ""{order.Code}"", {{
                    format: ""CODE128"",
                    width: 2,
                    height: 40,
                    displayValue: false,
                    margin: 0
                }});
            }} catch(e) {{
                console.error(e);
            }}
            setTimeout(function() {{
                window.print();
                window.onafterprint = function() {{
                    window.close();
                }};
            }}, 300);
        }};
    </script>
</body>
</html>";

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

    private class SettingJson
    {
        public string value { get; set; } = string.Empty;
    }
}
