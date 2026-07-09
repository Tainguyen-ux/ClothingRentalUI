using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClothingRentalUI.Data.Entities;
using ClothingRentalUI.Models.Common;

namespace ClothingRentalUI.Services;

public interface IOrderService
{
    Task<ApiResponse<Order>> CreateOrderAsync(Order order, int userId);
    Task<ApiResponse<Order>> GetByIdAsync(int id);
    Task<ApiResponse<IEnumerable<Order>>> GetOrdersAsync(DateTime? fromDate = null, DateTime? toDate = null, string? search = null);
    
    // Nghiệp vụ Cập nhật phát sinh (khi khách đang thuê)
    Task<ApiResponse> UpdatePenaltyAsync(int orderId, int detailId, int extendedDays, decimal penaltyFee, string? reason, int userId);
    
    // Nghiệp vụ Trả đồ & Đóng đơn hàng
    Task<ApiResponse> ReturnItemAsync(int orderId, int detailId, int userId);
    Task<ApiResponse> CloseOrderAsync(int orderId, int userId);
}
