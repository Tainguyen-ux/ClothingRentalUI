using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClothingRentalUI.Data.Entities;
using ClothingRentalUI.Models.Common;

namespace ClothingRentalUI.Services;

public interface IOrderService
{
    Task<ServiceResult<Order>> CreateOrderAsync(Order order, int userId);
    Task<ServiceResult<Order>> GetByIdAsync(int id);
    Task<ServiceResult<IEnumerable<Order>>> GetOrdersAsync(DateTime? fromDate = null, DateTime? toDate = null, string? search = null);
    
    // Nghiệp vụ Cập nhật phát sinh (khi khách đang thuê)
    Task<ServiceResult> UpdatePenaltyAsync(int orderId, int detailId, int extendedDays, decimal penaltyFee, string? reason, int userId);
    
    // Nghiệp vụ Trả đồ & Đóng đơn hàng
    Task<ServiceResult> ReturnItemAsync(int orderId, int detailId, int userId);
    Task<ServiceResult> CloseOrderAsync(int orderId, int userId);
}

