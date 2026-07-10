using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;
using ClothingRentalUI.Helpers;
using ClothingRentalUI.Models.Common;

namespace ClothingRentalUI.Services;

public class OrderService : IOrderService
{
    private readonly ClothingRentalDbContext _dbContext;

    public OrderService(ClothingRentalDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ServiceResult<Order>> CreateOrderAsync(Order order, int userId)
    {
        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            // Tự sinh mã đơn hàng: yyyyMMdd + 4 số thứ tự
            var todayStr = DateTime.Now.ToString("yyyyMMdd");
            var lastOrder = await _dbContext.Orders
                .Where(o => o.Code.StartsWith(todayStr))
                .OrderByDescending(o => o.Code)
                .FirstOrDefaultAsync();

            int nextSeq = 1;
            if (lastOrder != null && lastOrder.Code.Length >= 4)
            {
                var seqStr = lastOrder.Code.Substring(lastOrder.Code.Length - 4);
                if (int.TryParse(seqStr, out int lastSeq))
                {
                    nextSeq = lastSeq + 1;
                }
            }

            order.Code = $"{todayStr}{nextSeq:D4}";
            order.CreatedAt = DateTime.Now;
            order.CreatedByUserId = userId;

            decimal totalRent = 0;
            decimal totalDeposit = 0;

            foreach (var detail in order.OrderDetails)
            {
                var product = await _dbContext.Products
                    .Include(p => p.PriceList)
                    .FirstOrDefaultAsync(p => p.Id == detail.ProductId);

                if (product == null)
                {
                    return new ServiceResult<Order> { Success = false, Message = $"Sản phẩm ID {detail.ProductId} không tồn tại." };
                }

                if (order.Status == "Rented")
                {
                    if (product.StockQuantity < 1)
                    {
                        return new ServiceResult<Order> { Success = false, Message = $"Sản phẩm '{product.Name}' đã hết hàng, không thể cho thuê." };
                    }
                    // Trừ tồn kho sản phẩm khi khách bắt đầu thuê
                    product.StockQuantity -= 1;
                    if (product.StockQuantity <= 0)
                    {
                        product.IsAvailable = false;
                    }
                }

                // Gán giá thuê và tiền cọc tại thời điểm hiện tại từ PriceList
                detail.RentPrice = product.PriceList?.PricePerDay ?? 0;
                detail.Deposit = product.PriceList?.Deposit ?? 0;
                detail.IsReturned = false;

                totalRent += RentalRulesHelper.CalculateRentPrice(detail.RentPrice, detail.RentDays);
                totalDeposit += detail.Deposit;
            }

            order.TotalPrice = totalRent;
            order.TotalDeposit = totalDeposit;
            order.TotalPenalty = 0;
            order.FinalAmount = totalRent;

            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            return new ServiceResult<Order> { Success = true, Message = "Tạo đơn hàng thành công.", Data = order };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return new ServiceResult<Order> { Success = false, Message = $"Lỗi tạo đơn hàng: {ex.Message}" };
        }
    }

    public async Task<ServiceResult<Order>> GetByIdAsync(int id)
    {
        try
        {
            var order = await _dbContext.Orders
                .Include(o => o.CreatedByUser)
                .Include(o => o.ClosedByUser)
                .Include(o => o.OrderDetails)
                    .ThenInclude(od => od.Product)
                        .ThenInclude(p => p!.PriceList)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                return new ServiceResult<Order> { Success = false, Message = "Không tìm thấy đơn hàng." };
            }

            return new ServiceResult<Order> { Success = true, Data = order };
        }
        catch (Exception ex)
        {
            return new ServiceResult<Order> { Success = false, Message = $"Lỗi tìm đơn hàng: {ex.Message}" };
        }
    }

    public async Task<ServiceResult<IEnumerable<Order>>> GetOrdersAsync(DateTime? fromDate = null, DateTime? toDate = null, string? search = null)
    {
        try
        {
            var query = _dbContext.Orders
                .Include(o => o.CreatedByUser)
                .Include(o => o.OrderDetails)
                .AsQueryable();

            if (fromDate.HasValue)
            {
                query = query.Where(o => o.CreatedAt >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                query = query.Where(o => o.CreatedAt <= toDate.Value.Date.AddDays(1).AddTicks(-1));
            }

            if (!string.IsNullOrEmpty(search))
            {
                search = search.Trim().ToLower();
                query = query.Where(o => o.Code.ToLower().Contains(search)
                                      || (o.Customer != null && o.Customer.FullName.ToLower().Contains(search))
                                      || (o.Customer != null && o.Customer.PhoneNumber.ToLower().Contains(search))
                                      || (o.Customer != null && o.Customer.IdentityCard != null && o.Customer.IdentityCard.ToLower().Contains(search)));
            }

            var list = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
            return new ServiceResult<IEnumerable<Order>> { Success = true, Data = list };
        }
        catch (Exception ex)
        {
            return new ServiceResult<IEnumerable<Order>> { Success = false, Message = $"Lỗi lấy danh sách đơn hàng: {ex.Message}" };
        }
    }

    public async Task<ServiceResult> UpdatePenaltyAsync(int orderId, int detailId, int extendedDays, decimal penaltyFee, string? reason, int userId)
    {
        try
        {
            var order = await _dbContext.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
            {
                return new ServiceResult { Success = false, Message = "Không tìm thấy đơn hàng." };
            }

            if (order.Status == "Closed")
            {
                return new ServiceResult { Success = false, Message = "Đơn hàng đã đóng, không thể cập nhật phát sinh." };
            }

            var detail = order.OrderDetails.FirstOrDefault(od => od.Id == detailId);
            if (detail == null)
            {
                return new ServiceResult { Success = false, Message = "Không tìm thấy sản phẩm chi tiết trong đơn." };
            }

            detail.ExtendedDays = extendedDays;
            detail.PenaltyFee = penaltyFee;
            detail.PenaltyReason = reason;

            // Tính toán lại tổng tiền phát sinh cho toàn bộ đơn hàng
            order.TotalPenalty = order.OrderDetails.Sum(od => od.PenaltyFee);
            order.FinalAmount = order.TotalPrice + order.TotalPenalty;
            
            // PenaltyByUserId removed - tracked via Transactions
            // order.PenaltyByUserId = userId;

            await _dbContext.SaveChangesAsync();
            return new ServiceResult { Success = true, Message = "Cập nhật phát sinh cho sản phẩm thành công." };
        }
        catch (Exception ex)
        {
            return new ServiceResult { Success = false, Message = $"Lỗi cập nhật phát sinh: {ex.Message}" };
        }
    }

    public async Task<ServiceResult> ReturnItemAsync(int orderId, int detailId, int userId)
    {
        try
        {
            var order = await _dbContext.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
            {
                return new ServiceResult { Success = false, Message = "Không tìm thấy đơn hàng." };
            }

            if (order.Status == "Closed")
            {
                return new ServiceResult { Success = false, Message = "Đơn hàng đã đóng." };
            }

            var detail = order.OrderDetails.FirstOrDefault(od => od.Id == detailId);
            if (detail == null)
            {
                return new ServiceResult { Success = false, Message = "Không tìm thấy chi tiết sản phẩm." };
            }

            detail.IsReturned = true;
            await _dbContext.SaveChangesAsync();

            return new ServiceResult { Success = true, Message = "Xác nhận đã trả sản phẩm này." };
        }
        catch (Exception ex)
        {
            return new ServiceResult { Success = false, Message = $"Lỗi trả sản phẩm: {ex.Message}" };
        }
    }

    public async Task<ServiceResult> CloseOrderAsync(int orderId, int userId)
    {
        using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var order = await _dbContext.Orders
                .Include(o => o.OrderDetails)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
            {
                return new ServiceResult { Success = false, Message = "Không tìm thấy đơn hàng." };
            }

            if (order.Status == "Closed")
            {
                return new ServiceResult { Success = false, Message = "Đơn hàng này đã được đóng trước đó." };
            }

            // Tự động tính toán tiền phạt trễ hạn theo quy tắc hệ thống cho toàn bộ các sản phẩm chưa được tính phạt thủ công
            decimal totalCalculatedPenalty = 0;
            var returnDate = DateTime.Now;

            foreach (var detail in order.OrderDetails)
            {
                var product = await _dbContext.Products.FindAsync(detail.ProductId);
                if (product != null)
                {
                    // Trả lại hàng vào kho nếu chưa trả
                    if (!detail.IsReturned)
                    {
                        detail.IsReturned = true;
                    }

                    product.StockQuantity += 1;
                    product.IsAvailable = true;

                    // Tính toán phí phạt trễ hạn tự động (nếu người dùng chưa tự nhập phí phạt thủ công)
                    if (detail.PenaltyFee == 0)
                    {
                        decimal calculatedItemPenalty = RentalRulesHelper.CalculatePenalty(
                            detail.RentPrice, 
                            order.CreatedAt, 
                            detail.RentDays, 
                            returnDate
                        );
                        
                        if (calculatedItemPenalty > 0)
                        {
                            detail.PenaltyFee = calculatedItemPenalty;
                            detail.PenaltyReason = $"Tự động tính phạt trễ hạn {RentalRulesHelper.CalculateLateDays(order.CreatedAt, detail.RentDays, returnDate)} ngày.";
                        }
                    }

                    // Tích lũy doanh thu thuê cho sản phẩm
                    product.TotalRentRevenue += RentalRulesHelper.CalculateRentPrice(detail.RentPrice, detail.RentDays);
                }

                totalCalculatedPenalty += detail.PenaltyFee;
            }

            order.TotalPenalty = totalCalculatedPenalty;
            order.FinalAmount = order.TotalPrice + order.TotalPenalty;
            order.Status = "Closed";
            order.ActualReturnDate = returnDate;
            order.ClosedByUserId = userId;

            // Cách tính dòng tiền đối soát khách hàng:
            // Khoản cọc thu của khách: order.TotalDeposit
            // Tổng phí thuê + phạt khách phải chịu: order.FinalAmount
            // Số tiền hoàn trả khách = TotalDeposit - FinalAmount.
            // Nếu âm, tức khách phải trả thêm tiền.

            await _dbContext.SaveChangesAsync();
            await transaction.CommitAsync();

            var refundAmount = order.TotalDeposit - order.FinalAmount;
            var summaryMsg = refundAmount >= 0 
                ? $"Đóng đơn hàng thành công. Hoàn cọc trả khách: {FormatHelper.FormatCurrency(refundAmount)}."
                : $"Đóng đơn hàng thành công. Khách cần đóng thêm: {FormatHelper.FormatCurrency(Math.Abs(refundAmount))}.";

            return new ServiceResult { Success = true, Message = summaryMsg };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return new ServiceResult { Success = false, Message = $"Lỗi đóng đơn hàng: {ex.Message}" };
        }
    }
}

