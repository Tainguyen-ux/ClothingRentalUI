using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Models.Common;
using ClothingRentalUI.Models.Report;
using ClothingRentalUI.Models.Clothes;

namespace ClothingRentalUI.Services;

public class ReportService : IReportService
{
    private readonly ClothingRentalDbContext _dbContext;

    public ReportService(ClothingRentalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ServiceResult<ReportSummaryDto>> GetReportSummaryAsync(DateTime fromDate, DateTime toDate, int lowStockThreshold)
    {
        try
        {
            var start = fromDate.Date;
            var end = toDate.Date.AddDays(1).AddTicks(-1);

            // 1. Lọc đơn hàng trong khoảng thời gian
            var orders = await _dbContext.Orders
                .Include(o => o.CreatedByUser)
                .Where(o => o.CreatedAt >= start && o.CreatedAt <= end)
                .ToListAsync();

            var closedOrders = orders.Where(o => o.Status == "Closed").ToList();
            var openOrders = orders.Where(o => o.Status == "Rented").ToList();

            var summary = new ReportSummaryDto
            {
                // Doanh thu đơn đã đóng = Tổng tiền hàng + tiền phạt
                ClosedRevenue = closedOrders.Sum(o => o.FinalAmount),
                ClosedDeposit = closedOrders.Sum(o => o.TotalDeposit),
                
                // Doanh thu ước tính từ đơn chưa đóng = Tổng tiền hàng thuê gốc
                OpenEstimatedRevenue = openOrders.Sum(o => o.TotalPrice),
                OpenHeldDeposit = openOrders.Sum(o => o.TotalDeposit)
            };

            // 2. Thống kê theo nhân viên tạo đơn
            summary.UserRevenues = orders
                .GroupBy(o => o.CreatedByUser?.Username ?? "Unknown")
                .Select(g => new UserRevenueDto
                {
                    Username = g.Key,
                    TotalRevenue = g.Sum(o => o.Status == "Closed" ? o.FinalAmount : o.TotalPrice),
                    OrdersCount = g.Count()
                })
                .ToList();

            // 3. Danh sách CCCD đã nhận
            summary.ReceivedIdCards = orders
                .Where(o => o.Customer != null && !string.IsNullOrEmpty(o.Customer.IdentityCard))
                .Select(o => new IdCardReportDto
                {
                    CustomerName = o.Customer!.FullName,
                    PhoneNumber = o.Customer.PhoneNumber,
                    IdCardNumber = o.Customer.IdentityCard!,
                    OrderCode = o.Code,
                    IsClosed = o.Status == "Closed"
                })
                .ToList();

            // 4. Cảnh báo tồn kho dưới hạn mức
            summary.LowStockProducts = await _dbContext.Products
                .Include(p => p.PriceList)
                .Where(p => !p.IsLiquidated && p.StockQuantity < lowStockThreshold)
                .Select(p => new ClothesDto
                {
                    Id = p.Id,
                    Code = p.Code,
                    Name = p.Name,
                    Description = p.Description ?? string.Empty,
                    ImageUrl = p.ImageUrl ?? string.Empty,
                    PricePerDay = p.PriceList != null ? p.PriceList.PricePerDay : 0,
                    Deposit = p.PriceList != null ? p.PriceList.Deposit : 0,
                    Size = p.Size ?? string.Empty,
                    Color = p.Color ?? string.Empty,
                    StockQuantity = p.StockQuantity,
                    IsAvailable = p.IsAvailable && p.StockQuantity > 0,
                    CategoryName = _dbContext.Categories
                                       .Where(c => p.Code.StartsWith(c.CodePrefix))
                                       .Select(c => c.Name)
                                       .FirstOrDefault() ?? "Khác",
                    ImportPrice = p.ImportPrice,
                    TotalRentRevenue = p.TotalRentRevenue,
                    IsLiquidated = p.IsLiquidated
                })
                .ToListAsync();

            return new ServiceResult<ReportSummaryDto>
            {
                Success = true,
                Data = summary
            };
        }
        catch (Exception ex)
        {
            return new ServiceResult<ReportSummaryDto>
            {
                Success = false,
                Message = $"Lỗi tính toán báo cáo: {ex.Message}"
            };
        }
    }
}

