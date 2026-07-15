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
using ClothingRentalUI.Services;
using MiniExcelLibs;

namespace ClothingRentalUI.Pages.Products;

public class IndexModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public IndexModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public IList<Product> Products { get; set; } = new List<Product>();
    public IList<Category> Categories { get; set; } = new List<Category>();

    public class BarcodeConfigData
    {
        public int Width { get; set; } = 2;
        public int Height { get; set; } = 60;
        public int FontSize { get; set; } = 16;
    }
    public BarcodeConfigData BarcodeConfig { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public const int PageSize = 10;

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? CategoryId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }



    public List<string> CurrentUserPermissions { get; set; } = new();
    public bool IsAdmin { get; set; } = false;

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    private async Task<IActionResult?> VerifyAccessAsync(string requiredPermission = "CLOTHES_VIEW")
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username)) return RedirectToPage("/Auth/Login");

        var user = await _context.Users
            .Include(u => u.UserPermissions)
            .ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (user == null) return RedirectToPage("/Auth/Login");

        IsAdmin = user.Role == "Admin";
        CurrentUserPermissions = user.UserPermissions
            .Where(up => up.Permission != null)
            .Select(up => up.Permission!.Code)
            .ToList();

        if (!IsAdmin && !CurrentUserPermissions.Contains(requiredPermission))
        {
            return RedirectToPage("/Index");
        }
        return null;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // Self-healing: Ensure CLOTHES_EDIT, CLOTHES_LOCK, CLOTHES_DELETE exist
        var requiredPerms = new[] 
        { 
            new Permission { Code = "CLOTHES_EDIT", Name = "Sửa Sản phẩm", Type = "UI" },
            new Permission { Code = "CLOTHES_LOCK", Name = "Khóa Sản phẩm", Type = "UI" },
            new Permission { Code = "CLOTHES_DELETE", Name = "Xóa Sản phẩm", Type = "UI" }
        };
        
        bool needsSave = false;
        var existingPerms = await _context.Permissions.Select(p => p.Code).ToListAsync();
        foreach (var p in requiredPerms)
        {
            if (!existingPerms.Contains(p.Code))
            {
                _context.Permissions.Add(p);
                needsSave = true;
            }
        }
        if (needsSave)
        {
            await _context.SaveChangesAsync();
            var admins = await _context.Users.Where(u => u.Role == "Admin").ToListAsync();
            var newPerms = await _context.Permissions.Where(p => p.Code == "CLOTHES_EDIT" || p.Code == "CLOTHES_LOCK" || p.Code == "CLOTHES_DELETE").ToListAsync();
            foreach (var admin in admins)
            {
                foreach (var np in newPerms)
                {
                    if (!await _context.UserPermissions.AnyAsync(up => up.UserId == admin.Id && up.PermissionId == np.Id))
                    {
                        _context.UserPermissions.Add(new UserPermission { UserId = admin.Id, PermissionId = np.Id });
                    }
                }
            }
            await _context.SaveChangesAsync();
        }

        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        // Load Barcode Config
        var wStr = await _context.SystemSettings.Where(s => s.Key == "Barcode_Width").Select(s => s.ValueJson).FirstOrDefaultAsync();
        var hStr = await _context.SystemSettings.Where(s => s.Key == "Barcode_Height").Select(s => s.ValueJson).FirstOrDefaultAsync();
        var fsStr = await _context.SystemSettings.Where(s => s.Key == "Barcode_FontSize").Select(s => s.ValueJson).FirstOrDefaultAsync();

        if (!string.IsNullOrEmpty(wStr))
        {
            try { var obj = System.Text.Json.JsonSerializer.Deserialize<ClothingRentalUI.Pages.Settings.SystemSettingsModel.StandardSettingJson>(wStr); if (obj != null && int.TryParse(obj.value, out int w)) BarcodeConfig.Width = w; } catch {}
        }
        if (!string.IsNullOrEmpty(hStr))
        {
            try { var obj = System.Text.Json.JsonSerializer.Deserialize<ClothingRentalUI.Pages.Settings.SystemSettingsModel.StandardSettingJson>(hStr); if (obj != null && int.TryParse(obj.value, out int h)) BarcodeConfig.Height = h; } catch {}
        }
        if (!string.IsNullOrEmpty(fsStr))
        {
            try { var obj = System.Text.Json.JsonSerializer.Deserialize<ClothingRentalUI.Pages.Settings.SystemSettingsModel.StandardSettingJson>(fsStr); if (obj != null && int.TryParse(obj.value, out int fs)) BarcodeConfig.FontSize = fs; } catch {}
        }



        Categories = await _context.Categories.Where(c => c.IsActive).ToListAsync();

        var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.PriceList)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            query = query.Where(p => p.Name.ToLower().Contains(SearchTerm.ToLower()) || p.Code.ToLower().Contains(SearchTerm.ToLower()));
        }

        if (CategoryId.HasValue && CategoryId.Value > 0)
        {
            query = query.Where(p => p.CategoryId == CategoryId.Value);
        }

        if (!string.IsNullOrEmpty(Status))
        {
            switch (Status.ToLower())
            {
                case "active":
                    query = query.Where(p => !p.IsLiquidated && p.IsAvailable);
                    break;
                case "locked":
                    query = query.Where(p => !p.IsLiquidated && !p.IsAvailable);
                    break;
                case "liquidated":
                    query = query.Where(p => p.IsLiquidated);
                    break;
                case "lowstock":
                    query = query.Where(p => p.WarningStockLevel > 0 && p.StockQuantity <= p.WarningStockLevel);
                    break;
            }
        }

        TotalItems = await query.CountAsync();
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        Products = await query
            .OrderByDescending(p => p.Id)
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnGetExportExcelAsync()
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return authCheck;

        var query = _context.Products
            .Include(p => p.Category)
            .Include(p => p.PriceList)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            query = query.Where(p => p.Name.ToLower().Contains(SearchTerm.ToLower()) || p.Code.ToLower().Contains(SearchTerm.ToLower()));
        }

        if (CategoryId.HasValue && CategoryId.Value > 0)
        {
            query = query.Where(p => p.CategoryId == CategoryId.Value);
        }

        if (!string.IsNullOrEmpty(Status))
        {
            switch (Status.ToLower())
            {
                case "active":
                    query = query.Where(p => !p.IsLiquidated && p.IsAvailable);
                    break;
                case "locked":
                    query = query.Where(p => !p.IsLiquidated && !p.IsAvailable);
                    break;
                case "liquidated":
                    query = query.Where(p => p.IsLiquidated);
                    break;
                case "lowstock":
                    query = query.Where(p => p.WarningStockLevel > 0 && p.StockQuantity <= p.WarningStockLevel);
                    break;
            }
        }

        var list = await query.OrderByDescending(p => p.Id).ToListAsync();

        var excelData = list.Select((p, index) => {
            string statusStr = "Hoạt động";
            if (p.IsLiquidated) statusStr = "Đã thanh lý";
            else if (!p.IsAvailable) statusStr = "Đang khóa";

            return new Dictionary<string, object> {
                { "STT", index + 1 },
                { "Mã sản phẩm", p.Code },
                { "Tên sản phẩm", p.Name },
                { "Danh mục", p.Category?.Name ?? "" },
                { "Size", p.Size ?? "" },
                { "Màu sắc", p.Color ?? "" },
                { "Chất liệu", p.Material ?? "" },
                { "Tình trạng", p.Condition ?? "" },
                { "Giá nhập (đ)", p.ImportPrice },
                { "Giá thuê/ngày (đ)", p.PriceList?.PricePerDay ?? 0 },
                { "Giá trị cọc (đ)", p.PriceList?.Deposit ?? 0 },
                { "Tồn kho (cửa hàng)", p.StockQuantity },
                { "Đang cho thuê", p.RentedQuantity },
                { "Doanh thu thuê lũy kế (đ)", p.TotalRentRevenue },
                { "Trạng thái", statusStr }
            };
        }).ToList();

        var memoryStream = new MemoryStream();
        memoryStream.SaveAs(excelData);
        memoryStream.Seek(0, SeekOrigin.Begin);

        var fileName = $"DanhSachSanPham_{DateTime.UtcNow.AddHours(7):yyyyMMdd_HHmmss}.xlsx";
        return File(memoryStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    public async Task<IActionResult> OnPostToggleStatusAsync(int id)
    {
        var authCheck = await VerifyAccessAsync("CLOTHES_LOCK");
        if (authCheck != null) return authCheck;

        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            ErrorMessage = "Không tìm thấy sản phẩm.";
            return RedirectToPage();
        }

        product.IsAvailable = !product.IsAvailable;
        await _context.SaveChangesAsync();
        SuccessMessage = $"Đã {(product.IsAvailable ? "mở khóa" : "tạm khóa")} sản phẩm thành công.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var authCheck = await VerifyAccessAsync("CLOTHES_DELETE");
        if (authCheck != null) return authCheck;

        var product = await _context.Products.FindAsync(id);
        if (product == null)
        {
            ErrorMessage = "Không tìm thấy sản phẩm.";
            return RedirectToPage();
        }

        var hasOrders = await _context.OrderDetails.AnyAsync(od => od.ProductId == id);
        if (hasOrders)
        {
            ErrorMessage = "Không thể xóa sản phẩm đã có lịch sử thuê. Bạn chỉ có thể Khóa sản phẩm này.";
            return RedirectToPage();
        }

        var histories = await _context.StockHistories.Where(h => h.ProductId == id).ToListAsync();
        _context.StockHistories.RemoveRange(histories);

        // Delete local image files from disk
        if (!string.IsNullOrEmpty(product.ImageUrl))
        {
            var urls = new List<string>();
            if (product.ImageUrl.Trim().StartsWith("[") && product.ImageUrl.Trim().EndsWith("]"))
            {
                try
                {
                    urls = System.Text.Json.JsonSerializer.Deserialize<List<string>>(product.ImageUrl) ?? new List<string>();
                }
                catch
                {
                    urls.Add(product.ImageUrl);
                }
            }
            else
            {
                urls.Add(product.ImageUrl);
            }

            foreach (var url in urls)
            {
                if (string.IsNullOrWhiteSpace(url)) continue;
                if (url.StartsWith("/") || url.Contains("uploads"))
                {
                    var relativePath = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                    var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", relativePath);
                    if (System.IO.File.Exists(fullPath))
                    {
                        try { System.IO.File.Delete(fullPath); } catch {}
                    }
                }
            }
        }

        _context.Products.Remove(product);
        
        await _context.SaveChangesAsync();
        SuccessMessage = $"Đã xóa sản phẩm {product.Code} khỏi hệ thống.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetRentalHistoryAsync(int productId, int pageIndex = 1, string? startDate = null, string? endDate = null)
    {
        var authCheck = await VerifyAccessAsync();
        if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền truy cập." });

        var query = _context.OrderDetails
            .Include(od => od.Order)
            .ThenInclude(o => o!.Customer)
            .Where(od => od.ProductId == productId);

        if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out var startVal))
        {
            var startUtc = DateTime.SpecifyKind(startVal.Date, DateTimeKind.Utc);
            query = query.Where(od => od.Order != null && od.Order.CreatedAt >= startUtc);
        }

        if (!string.IsNullOrEmpty(endDate) && DateTime.TryParse(endDate, out var endVal))
        {
            var endUtc = DateTime.SpecifyKind(endVal.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(od => od.Order != null && od.Order.CreatedAt <= endUtc);
        }

        var totalItems = await query.CountAsync();
        int pageSize = 5;
        int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);
        if (pageIndex < 1) pageIndex = 1;
        if (totalPages > 0 && pageIndex > totalPages) pageIndex = totalPages;

        var history = await query
            .OrderByDescending(od => od.Order != null ? od.Order.CreatedAt : DateTime.MinValue)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(od => new
            {
                orderId = od.Order != null ? od.Order.Code : "",
                customerName = od.Order != null && od.Order.Customer != null ? od.Order.Customer.FullName : "Khách vãng lai",
                createdAt = od.Order != null ? od.Order.CreatedAt : DateTime.MinValue,
                rentDays = od.RentDays,
                extendedDays = od.ExtendedDays,
                isReturned = od.IsReturned
            })
            .ToListAsync();

        return new JsonResult(new { success = true, history, pageIndex, totalPages, totalItems });
    }

    public class UpdateImageRequest
    {
        public int ProductId { get; set; }
        public string Url { get; set; } = string.Empty;
    }

    public async Task<IActionResult> OnPostUpdateImageAjaxAsync([FromBody] UpdateImageRequest request)
    {
        try
        {
            var authCheck = await VerifyAccessAsync("CLOTHES_EDIT");
            if (authCheck != null) return new JsonResult(new { success = false, message = "Không có quyền chỉnh sửa sản phẩm." });

            if (request == null || request.ProductId <= 0)
                return new JsonResult(new { success = false, message = "Dữ liệu không hợp lệ." });

            var product = await _context.Products.FindAsync(request.ProductId);
            if (product == null)
                return new JsonResult(new { success = false, message = "Không tìm thấy sản phẩm." });

            product.ImageUrl = request.Url;
            await _context.SaveChangesAsync();

            return new JsonResult(new { success = true, message = "Cập nhật hình ảnh thành công." });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = $"Lỗi máy chủ: {ex.Message}" });
        }
    }

    public static string GetDirectGoogleDriveImageUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return string.Empty;
        
        string targetUrl = url;
        // Check if it's a JSON array
        if (url.Trim().StartsWith("[") && url.Trim().EndsWith("]"))
        {
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(url);
                if (list != null && list.Count > 0)
                {
                    targetUrl = list[0];
                }
                else
                {
                    return string.Empty;
                }
            }
            catch
            {
                // Fallback to single url
            }
        }

        return targetUrl;
    }

    public async Task<IActionResult> OnPostUploadLocalImageAsync(IFormFile file)
    {
        try
        {
            var authCheck = await VerifyAccessAsync("CLOTHES_EDIT");
            if (authCheck != null) return new JsonResult(new { success = false, error = "Không có quyền truy cập." });

            if (file == null || file.Length == 0)
            {
                return new JsonResult(new { success = false, error = "Tệp tin không hợp lệ." });
            }

            var ext = System.IO.Path.GetExtension(file.FileName).ToLower();

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

    public async Task<IActionResult> OnPostImportExcelAsync(IFormFile excelFile)
    {
        var authCheck = await VerifyAccessAsync("CLOTHES_CREATE");
        if (authCheck != null) return new JsonResult(new { success = false, error = "Bạn không có quyền thực hiện chức năng này." });

        if (excelFile == null || excelFile.Length == 0)
        {
            return new JsonResult(new { success = false, error = "Vui lòng chọn tệp tin Excel để tải lên." });
        }

        var ext = Path.GetExtension(excelFile.FileName).ToLower();
        if (ext != ".xlsx" && ext != ".xls")
        {
            return new JsonResult(new { success = false, error = "Chỉ chấp nhận tệp tin định dạng Excel (.xlsx, .xls)." });
        }

        var tempFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ext);
        using (var stream = new FileStream(tempFilePath, FileMode.Create))
        {
            await excelFile.CopyToAsync(stream);
        }

        int categoriesImported = 0;
        int productsImported = 0;
        var errors = new List<string>();

        try
        {
            var sheetNames = MiniExcel.GetSheetNames(tempFilePath);
            var categorySheetName = sheetNames.FirstOrDefault(s => {
                var norm = RemoveAccents(s.ToLower().Replace(" ", ""));
                return norm.Contains("danhmuc") || norm.Contains("loaihang") || norm.Contains("category");
            }) ?? sheetNames.FirstOrDefault();

            var productSheetName = sheetNames.FirstOrDefault(s => {
                var norm = RemoveAccents(s.ToLower().Replace(" ", ""));
                return norm.Contains("nhaphang") || norm.Contains("sanpham") || norm.Contains("product") || norm.Contains("hanghoa") || norm.Contains("hang");
            });

            if (string.IsNullOrEmpty(productSheetName))
            {
                if (sheetNames.Count > 1)
                {
                    productSheetName = sheetNames.FirstOrDefault(s => s != categorySheetName) ?? sheetNames.ElementAtOrDefault(1);
                }
                else
                {
                    productSheetName = categorySheetName;
                }
            }

            errors.Add($"[Thông tin Import] Tìm thấy các Sheet: {string.Join(", ", sheetNames)}");
            errors.Add($"[Thông tin Import] Đang đọc Loại hàng từ Sheet: '{categorySheetName}'");
            errors.Add($"[Thông tin Import] Đang đọc Hàng hóa từ Sheet: '{productSheetName}'");

            var categoryCache = await _context.Categories.ToDictionaryAsync(c => c.CodePrefix.ToUpper(), c => c);
            var priceListCache = await _context.PriceLists.ToDictionaryAsync(p => p.Name.ToUpper(), p => p);

            // 1. Process Categories & PriceLists
            if (!string.IsNullOrEmpty(categorySheetName))
            {
                var catRows = MiniExcel.Query(tempFilePath, useHeaderRow: true, sheetName: categorySheetName);
                int rowIndex = 1;
                foreach (var r in catRows)
                {
                    rowIndex++;
                    if (r == null) continue;

                    var dict = r as IDictionary<string, object>;
                    if (dict == null) continue;

                    if (rowIndex == 2)
                    {
                        errors.Add($"[Cột phát hiện - Loại hàng]: {string.Join(", ", dict.Keys)}");
                    }

                    var codePrefix = GetValue(dict, "ma loai hang", "maloaihang", "ma loai", "maloai", "code prefix", "prefix", "loai hang", "loaihang");
                    var name = GetValue(dict, "ten loai hang", "tenloaihang", "ten loai", "tenloai", "name", "category name", "ten loai hang hoa", "tenloaihanghoa");
                    var priceListName = GetValue(dict, "ma gia tien", "magiatien", "ma gia", "magia", "gia tien", "giatien", "price code", "price", "loai gia", "bang gia");

                    if (string.IsNullOrWhiteSpace(codePrefix) && string.IsNullOrWhiteSpace(name)) continue;

                    if (string.IsNullOrWhiteSpace(codePrefix))
                    {
                        errors.Add($"[Sheet Danh mục] Dòng {rowIndex}: Mã loại hàng không được để trống.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        errors.Add($"[Sheet Danh mục] Dòng {rowIndex} (Mã: {codePrefix}): Tên loại hàng không được để trống.");
                        continue;
                    }

                    try
                    {
                        var prefixUpper = codePrefix.ToUpper().Trim();
                        if (!categoryCache.TryGetValue(prefixUpper, out var category))
                        {
                            category = new Category
                            {
                                CodePrefix = prefixUpper,
                                Name = name.Trim(),
                                Description = "Nhập từ Excel",
                                IsActive = true,
                                SystemLog = "[]",
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            };
                            _context.Categories.Add(category);
                            categoryCache[prefixUpper] = category;
                            categoriesImported++;
                        }
                        else
                        {
                            if (category.Name != name.Trim())
                            {
                                category.Name = name.Trim();
                                category.UpdatedAt = DateTime.UtcNow;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(priceListName))
                        {
                            var priceListUpper = priceListName.ToUpper().Trim();
                            if (!priceListCache.TryGetValue(priceListUpper, out var priceList))
                            {
                                var parsedPrice = ParsePriceFromCode(priceListName) ?? 0;
                                priceList = new PriceList
                                {
                                    Name = priceListName.Trim(),
                                    PricePerDay = parsedPrice,
                                    Deposit = parsedPrice * 3,
                                    Description = "Tạo tự động khi nhập danh mục hàng",
                                    IsActive = true,
                                    CreatedAt = DateTime.UtcNow,
                                    UpdatedAt = DateTime.UtcNow
                                };
                                _context.PriceLists.Add(priceList);
                                priceListCache[priceListUpper] = priceList;
                            }
                        }

                        await _context.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        foreach (var entry in _context.ChangeTracker.Entries().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
                        {
                            entry.State = EntityState.Detached;
                        }
                        errors.Add($"[Sheet Danh mục] Dòng {rowIndex} (Mã: {codePrefix}): Lỗi lưu cơ sở dữ liệu: {ex.Message}");
                    }
                }
            }

            // Refresh caches to make sure all newly created items are included with valid IDs
            categoryCache = await _context.Categories.ToDictionaryAsync(c => c.CodePrefix.ToUpper(), c => c);
            priceListCache = await _context.PriceLists.ToDictionaryAsync(p => p.Name.ToUpper(), p => p);

            // 2. Process Products
            if (!string.IsNullOrEmpty(productSheetName))
            {
                var prodRows = MiniExcel.Query(tempFilePath, useHeaderRow: true, sheetName: productSheetName);
                int rowIndex = 1;
                foreach (var r in prodRows)
                {
                    rowIndex++;
                    if (r == null) continue;

                    var dict = r as IDictionary<string, object>;
                    if (dict == null) continue;

                    if (rowIndex == 2)
                    {
                        errors.Add($"[Cột phát hiện - Hàng hóa]: {string.Join(", ", dict.Keys)}");
                    }

                    var stt = GetValue(dict, "stt", "index", "no");
                    var categoryPrefix = GetValue(dict, "ma loai hang", "maloaihang", "ma loai", "maloai", "code prefix", "prefix", "loai hang", "loaihang", "category");
                    var productCode = GetValue(dict, "ma hang", "mahang", "code", "product code", "ma sp", "masp", "ma hang hoa", "mahanghoa");
                    var priceListName = GetValue(dict, "gia tien", "giatien", "price code", "magiatien", "ma gia tien", "gia", "loai gia", "bang gia");
                    var productName = GetValue(dict, "ten hang", "tenhang", "ten san pham", "tensanpham", "name", "product name", "ten hang hoa", "tenhanghoa");
                    var importPriceStr = GetValue(dict, "gia nhap", "gianhap", "import price", "cost", "gia mua", "giamua");
                    var rentalPriceStr = GetValue(dict, "gia cho thue", "giachothue", "rental price", "price per day", "priceperday", "gia thue", "giathue");
                    var quantityStr = GetValue(dict, "so luong", "soluong", "quantity", "qty", "stock", "ton kho", "ton", "so luong nhap");
                    var warningStr = GetValue(dict, "canh bao", "canhbaoton", "canh baoton", "canhbaotonkho", "warning stock level", "warning", "nguong canh bao", "nguongcanhbao", "minimum stock", "min stock", "minstock");

                    if (string.IsNullOrWhiteSpace(productCode) && string.IsNullOrWhiteSpace(productName) && string.IsNullOrWhiteSpace(categoryPrefix))
                    {
                        continue;
                    }

                    string identifier = !string.IsNullOrWhiteSpace(stt) ? $"STT {stt}" : $"Dòng {rowIndex}";

                    if (string.IsNullOrWhiteSpace(productCode))
                    {
                        errors.Add($"[Sheet Nhập hàng] {identifier}: Mã sản phẩm không được bỏ trống.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(productName))
                    {
                        errors.Add($"[Sheet Nhập hàng] {identifier} (Mã: {productCode}): Tên sản phẩm không được bỏ trống.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(categoryPrefix))
                    {
                        errors.Add($"[Sheet Nhập hàng] {identifier} (Mã: {productCode}): Mã loại hàng không được bỏ trống.");
                        continue;
                    }

                    var prefixUpper = categoryPrefix.ToUpper().Trim();
                    if (!categoryCache.TryGetValue(prefixUpper, out var category))
                    {
                        errors.Add($"[Sheet Nhập hàng] {identifier} (Mã: {productCode}): Loại hàng '{categoryPrefix}' chưa tồn tại trong danh mục.");
                        continue;
                    }

                    try
                    {
                        int priceListId = 0;
                        if (!string.IsNullOrWhiteSpace(priceListName))
                        {
                            var priceListUpper = priceListName.ToUpper().Trim();
                            if (priceListCache.TryGetValue(priceListUpper, out var priceList))
                            {
                                priceListId = priceList.Id;
                            }
                            else
                            {
                                var parsedPrice = ParsePriceFromCode(rentalPriceStr) ?? ParsePriceFromCode(priceListName) ?? 0;
                                priceList = new PriceList
                                {
                                    Name = priceListName.Trim(),
                                    PricePerDay = parsedPrice,
                                    Deposit = parsedPrice * 3,
                                    Description = "Tạo tự động khi nhập hàng",
                                    IsActive = true,
                                    CreatedAt = DateTime.UtcNow,
                                    UpdatedAt = DateTime.UtcNow
                                };
                                _context.PriceLists.Add(priceList);
                                await _context.SaveChangesAsync();
                                priceListCache[priceListUpper] = priceList;
                                priceListId = priceList.Id;
                            }
                        }
                        else
                        {
                            var parsedPrice = ParsePriceFromCode(rentalPriceStr) ?? 0;
                            string generatedName = $"{parsedPrice / 1000}K";
                            var priceListUpper = generatedName.ToUpper();
                            if (priceListCache.TryGetValue(priceListUpper, out var priceList))
                            {
                                priceListId = priceList.Id;
                            }
                            else
                            {
                                priceList = new PriceList
                                {
                                    Name = generatedName,
                                    PricePerDay = parsedPrice,
                                    Deposit = parsedPrice * 3,
                                    Description = "Tạo tự động khi nhập hàng",
                                    IsActive = true,
                                    CreatedAt = DateTime.UtcNow,
                                    UpdatedAt = DateTime.UtcNow
                                };
                                _context.PriceLists.Add(priceList);
                                await _context.SaveChangesAsync();
                                priceListCache[priceListUpper] = priceList;
                                priceListId = priceList.Id;
                            }
                        }

                        var importPrice = ParsePriceFromCode(importPriceStr) ?? 0;
                        int quantity = 0;
                        if (!string.IsNullOrWhiteSpace(quantityStr))
                        {
                            var cleanQtyStr = quantityStr.Split('.')[0].Split(',')[0].Trim();
                            int.TryParse(cleanQtyStr, out quantity);
                        }

                        if (quantity <= 0)
                        {
                            errors.Add($"[Sheet Nhập hàng] {identifier} (Mã: {productCode}): Số lượng nhập '{quantityStr}' không hợp lệ (phải > 0).");
                            continue;
                        }

                        int warningStockLevel = 0;
                        if (!string.IsNullOrWhiteSpace(warningStr))
                        {
                            var cleanWarningStr = warningStr.Split('.')[0].Split(',')[0].Trim();
                            int.TryParse(cleanWarningStr, out warningStockLevel);
                        }

                        var product = await _context.Products.FirstOrDefaultAsync(p => p.Code.ToLower() == productCode.ToLower().Trim());
                        if (product != null)
                        {
                            product.Name = productName.Trim();
                            product.CategoryId = category.Id;
                            product.PriceListId = priceListId;
                            product.ImportPrice = importPrice;
                            product.StockQuantity += quantity;
                            product.IsAvailable = product.StockQuantity > 0;
                            if (!string.IsNullOrWhiteSpace(warningStr))
                            {
                                product.WarningStockLevel = warningStockLevel;
                            }
                        }
                        else
                        {
                            product = new Product
                            {
                                Code = productCode.ToUpper().Trim(),
                                Name = productName.Trim(),
                                CategoryId = category.Id,
                                PriceListId = priceListId,
                                ImportPrice = importPrice,
                                StockQuantity = quantity,
                                RentedQuantity = 0,
                                ImageUrl = "[]",
                                IsAvailable = true,
                                IsLiquidated = false,
                                SystemLog = "[]",
                                TotalRentRevenue = 0,
                                WarningStockLevel = warningStockLevel
                            };
                            _context.Products.Add(product);
                        }

                        await _context.SaveChangesAsync();

                        var history = new StockHistory
                        {
                            ProductId = product.Id,
                            ActionType = "IMPORT",
                            QuantityChange = quantity,
                            RemainingTotal = product.StockQuantity,
                            ReferenceCode = "EXCEL_IMPORT",
                            Note = $"Nhập hàng nhanh từ Excel ({identifier})",
                            PerformedBy = HttpContext.Session.GetString("Username") ?? "system",
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.StockHistories.Add(history);
                        await _context.SaveChangesAsync();

                        productsImported++;
                    }
                    catch (Exception ex)
                    {
                        foreach (var entry in _context.ChangeTracker.Entries().Where(e => e.State == EntityState.Added || e.State == EntityState.Modified))
                        {
                            entry.State = EntityState.Detached;
                        }
                        errors.Add($"[Sheet Nhập hàng] {identifier} (Mã: {productCode}): Lỗi cơ sở dữ liệu: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, error = $"Lỗi khi đọc file Excel: {ex.Message}" });
        }
        finally
        {
            if (System.IO.File.Exists(tempFilePath))
            {
                System.IO.File.Delete(tempFilePath);
            }
        }

        return new JsonResult(new { 
            success = true, 
            categoriesCount = categoriesImported, 
            productsCount = productsImported, 
            errors = errors 
        });
    }

    private string RemoveAccents(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return new string(
            text.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()
        ).Normalize(System.Text.NormalizationForm.FormC)
        .Replace("đ", "d")
        .Replace("Đ", "D");
    }

    private decimal? ParsePriceFromCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var clean = code.Trim().ToLower();
        clean = clean.Replace(".", "")
                     .Replace(",", "")
                     .Replace("đ", "")
                     .Replace("d", "")
                     .Replace("vnd", "")
                     .Replace(" ", "");

        if (clean.EndsWith("k"))
        {
            var numPart = clean.Substring(0, clean.Length - 1);
            if (decimal.TryParse(numPart, out var kVal))
            {
                return kVal * 1000;
            }
        }
        else
        {
            if (decimal.TryParse(clean, out var val))
            {
                return val;
            }
        }
        return null;
    }

    private string GetValue(IDictionary<string, object> dict, params string[] possibleKeys)
    {
        var normalizedPossibleKeys = possibleKeys.Select(pk => RemoveAccents(pk.Trim().ToLower().Replace(" ", "").Replace("_", ""))).ToList();
        foreach (var entry in dict)
        {
            var normalizedKey = RemoveAccents(entry.Key.Trim().ToLower().Replace(" ", "").Replace("_", ""));
            if (normalizedPossibleKeys.Contains(normalizedKey))
            {
                return entry.Value?.ToString()?.Trim() ?? string.Empty;
            }
        }
        return string.Empty;
    }
}
