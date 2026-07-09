using System.Collections.Generic;
using ClothingRentalUI.Models.Clothes;

namespace ClothingRentalUI.Models.Report;

public class ReportSummaryDto
{
    public decimal ClosedRevenue { get; set; } // Doanh thu từ đơn đã đóng
    public decimal ClosedDeposit { get; set; } // Tiền cọc từ đơn đã đóng
    public decimal OpenEstimatedRevenue { get; set; } // Doanh thu ước tính từ đơn chưa đóng
    public decimal OpenHeldDeposit { get; set; } // Tiền cọc đang giữ của đơn chưa đóng

    public List<UserRevenueDto> UserRevenues { get; set; } = new();
    public List<IdCardReportDto> ReceivedIdCards { get; set; } = new();
    public List<ClothesDto> LowStockProducts { get; set; } = new();
}

public class UserRevenueDto
{
    public string Username { get; set; } = string.Empty;
    public decimal TotalRevenue { get; set; }
    public int OrdersCount { get; set; }
}

public class IdCardReportDto
{
    public string CustomerName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string IdCardNumber { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;
    public bool IsClosed { get; set; }
}
