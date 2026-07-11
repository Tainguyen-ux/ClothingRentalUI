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

public class TestVoucherModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public TestVoucherModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public string TestResults { get; set; } = "";

    public async Task OnGetAsync()
    {
        var logs = new List<string>();
        logs.Add("--- BẮT ĐẦU KIỂM TRA TÍCH HỢP VOUCHER ---");

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 0. Tạo tài khoản admin kiểm thử nếu chưa có
            var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == "admin");
            if (adminUser == null)
            {
                adminUser = new User
                {
                    Username = "admin",
                    PasswordHash = "fakehash",
                    Role = "Admin",
                    FullName = "Administrator"
                };
                _context.Users.Add(adminUser);
                await _context.SaveChangesAsync();
                logs.Add("Đã tạo tài khoản admin kiểm thử");
            }
            else
            {
                logs.Add("Đã có tài khoản admin kiểm thử");
            }

            // Thiết lập session để vượt qua VerifyAccessAsync
            HttpContext.Session.SetString("Username", "admin");

            // 1. Tạo hoặc lấy khách hàng kiểm thử
            var customer = await _context.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == "0999999999");
            if (customer == null)
            {
                customer = new Customer
                {
                    FullName = "Khách Hàng Kiểm Thử",
                    PhoneNumber = "0999999999",
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
                logs.Add($"Đã tạo khách hàng kiểm thử ID: {customer.Id}");
            }
            else
            {
                logs.Add($"Sử dụng khách hàng kiểm thử sẵn có ID: {customer.Id}");
            }

            // 2. Tạo hoặc lấy bảng giá
            var priceList = await _context.PriceLists.FirstOrDefaultAsync(p => p.Name == "Giá Kiểm Thử");
            if (priceList == null)
            {
                priceList = new PriceList
                {
                    Name = "Giá Kiểm Thử",
                    PricePerDay = 100000, // 100k/ngày
                    Deposit = 200000,    // cọc 200k
                    CreatedAt = DateTime.UtcNow
                };
                _context.PriceLists.Add(priceList);
                await _context.SaveChangesAsync();
                logs.Add("Đã tạo bảng giá kiểm thử");
            }

            // 2.5 Tạo hoặc lấy Category
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Name == "Danh Mục Kiểm Thử");
            if (category == null)
            {
                category = new Category
                {
                    Name = "Danh Mục Kiểm Thử",
                    CodePrefix = "TESTCAT",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Categories.Add(category);
                await _context.SaveChangesAsync();
                logs.Add("Đã tạo danh mục kiểm thử");
            }

            // 3. Tạo hoặc lấy sản phẩm
            var product = await _context.Products.FirstOrDefaultAsync(p => p.Code == "TESTPROD");
            if (product == null)
            {
                product = new Product
                {
                    Code = "TESTPROD",
                    Name = "Váy Kiểm Thử",
                    CategoryId = category.Id,
                    PriceListId = priceList.Id,
                    StockQuantity = 10,
                    RentedQuantity = 0,
                    IsAvailable = true
                };
                _context.Products.Add(product);
                await _context.SaveChangesAsync();
                logs.Add($"Đã tạo sản phẩm kiểm thử ID: {product.Id}");
            }
            else
            {
                product.StockQuantity = 10;
                product.RentedQuantity = 0;
                product.IsAvailable = true;
                _context.Products.Update(product);
                await _context.SaveChangesAsync();
                logs.Add($"Sử dụng sản phẩm kiểm thử sẵn có ID: {product.Id} (Reset stock)");
            }

            // 4. Tạo hoặc lấy voucher
            var voucherCode = "TESTVOUCHER" + DateTime.UtcNow.Ticks.ToString().Substring(10);
            var voucher = new Voucher
            {
                Code = voucherCode,
                Name = "Voucher Giảm 10% Tối Đa 50k",
                DiscountType = "PERCENT",
                DiscountValue = 10,
                MaxDiscountAmount = 50000,
                MinOrderAmount = 150000,
                MaxUsageCount = 5,
                UsedCount = 0,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(7),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.Vouchers.Add(voucher);
            await _context.SaveChangesAsync();
            logs.Add($"Đã tạo voucher mới với mã: {voucherCode}");

            // 5. Test Case 1: Thử tạo đơn hàng với giá trị thấp hơn MinOrderAmount
            logs.Add("\n--- Test Case 1: Áp dụng voucher khi đơn hàng không đạt giá trị tối thiểu ---");
            var createModel = new CreateModel(_context)
            {
                PageContext = this.PageContext
            };

            var req1 = new CreateModel.CreateOrderRequest
            {
                CustomerId = customer.Id,
                RentDays = 1,
                VoucherCode = voucherCode,
                Items = new List<CreateModel.OrderItemRequest>
                {
                    new CreateModel.OrderItemRequest
                    {
                        ProductId = product.Id,
                        Quantity = 1,
                        RentDays = 1,
                        RentPrice = 100000, // 100k < 150k min order
                        Deposit = 200000
                    }
                }
            };
            
            var res1 = await createModel.OnPostCreateOrderAjaxAsync(req1) as JsonResult;
            dynamic data1 = res1.Value;
            logs.Add($"Kết quả: success = {data1.success}, message = {data1.message}");
            if (data1.success == false && data1.message.Contains("tối thiểu"))
            {
                logs.Add("=> ĐẠT (Từ chối chính xác)");
            }
            else
            {
                logs.Add("=> THẤT BẠI");
            }

            // 6. Test Case 2: Tạo đơn hàng thành công với voucher hợp lệ
            logs.Add("\n--- Test Case 2: Áp dụng voucher thành công cho đơn hàng hợp lệ ---");
            var req2 = new CreateModel.CreateOrderRequest
            {
                CustomerId = customer.Id,
                RentDays = 2,
                VoucherCode = voucherCode,
                Items = new List<CreateModel.OrderItemRequest>
                {
                    new CreateModel.OrderItemRequest
                    {
                        ProductId = product.Id,
                        Quantity = 1,
                        RentDays = 2,
                        RentPrice = 100000, // Tổng 200k >= 150k min order
                        Deposit = 200000
                    }
                }
            };

            var res2 = await createModel.OnPostCreateOrderAjaxAsync(req2) as JsonResult;
            dynamic data2 = res2.Value;
            logs.Add($"Kết quả: success = {data2.success}");
            if (data2.success == true)
            {
                int orderId = data2.orderId;
                logs.Add($"=> ĐẠT (Tạo đơn thành công ID: {orderId})");

                // Kiểm tra thuộc tính của đơn hàng trong DB
                var order = await _context.Orders.Include(o => o.Voucher).FirstOrDefaultAsync(o => o.Id == orderId);
                logs.Add($"Tổng tiền gốc: {order.TotalPrice:N0}₫");
                logs.Add($"Tiền giảm giá: {order.DiscountAmount:N0}₫");
                logs.Add($"Tiền thanh toán cuối: {order.FinalAmount:N0}₫");
                logs.Add($"Mã voucher trong đơn hàng: {order.Voucher?.Code}");

                // Kiểm tra UsedCount của voucher
                var updatedVoucher = await _context.Vouchers.FirstOrDefaultAsync(v => v.Id == voucher.Id);
                logs.Add($"Số lượt sử dụng của voucher hiện tại: {updatedVoucher.UsedCount}");

                if (order.DiscountAmount == 20000 && order.FinalAmount == 380000 && updatedVoucher.UsedCount == 1) // 200k rent - 20k discount + 200k deposit = 380k final amount
                {
                    logs.Add("=> ĐẠT (Các giá trị tính toán và UsedCount chính xác)");
                }
                else
                {
                    logs.Add("=> THẤT BẠI");
                }
            }
            else
            {
                logs.Add($"=> THẤT BẠI: {data2.message}");
            }

            // Hoàn tác các thay đổi để tránh làm bẩn database
            await transaction.RollbackAsync();
            logs.Add("\nĐã hoàn tác giao dịch kiểm thử thành công (Database sạch).");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logs.Add($"LỖI TRONG QUÁ TRÌNH TEST: {ex.ToString()}");
        }

        TestResults = string.Join("\n", logs);
    }
}
