using System;

namespace ClothingRentalUI.Helpers;

public static class RentalRulesHelper
{
    private const decimal DailyLateFee = 10000; // 10k/ngày trễ

    /// <summary>
    /// Tính toán tổng tiền thuê gốc dựa trên đơn giá và số ngày thuê mong muốn.
    /// </summary>
    public static decimal CalculateRentPrice(decimal pricePerDay, int rentDays)
    {
        if (rentDays <= 0) return 0;
        return pricePerDay * rentDays;
    }

    /// <summary>
    /// Tính số ngày trễ hạn thực tế dựa trên ngày thuê, số ngày thuê dự kiến và ngày trả thực tế.
    /// Giá thuê áp dụng cho 1 ngày: ngày thuê + 1 (Grace period = rentDays + 1).
    /// </summary>
    public static int CalculateLateDays(DateTime rentDate, int expectedRentDays, DateTime actualReturnDate)
    {
        // Ngày được trả đồ không bị phạt trễ hạn
        var gracePeriodEndDate = rentDate.Date.AddDays(expectedRentDays + 1);
        
        var actualDate = actualReturnDate.Date;
        if (actualDate <= gracePeriodEndDate)
        {
            return 0;
        }

        return (actualDate - gracePeriodEndDate).Days;
    }

    /// <summary>
    /// Tính tiền phạt dựa trên số ngày trễ và giá thuê gốc của mặt hàng.
    /// Quy tắc:
    /// - Trễ hạn: +10,000 VND / ngày.
    /// - Trễ từ ngày thứ 4 trở đi (ngày thuê + 4): Tự động cộng thêm giá cho thuê cơ bản ban đầu của mặt hàng đó.
    /// </summary>
    public static decimal CalculatePenalty(decimal basePricePerDay, DateTime rentDate, int expectedRentDays, DateTime actualReturnDate, decimal lateFeePerDay = 10000, int thresholdDays = 4)
    {
        int lateDays = CalculateLateDays(rentDate, expectedRentDays, actualReturnDate);
        if (lateDays <= 0)
        {
            return 0;
        }

        // Tính tiền phạt trễ hạn theo ngày
        decimal penalty = lateDays * lateFeePerDay;

        // Nếu qua ngày ngưỡng trễ hạn, cộng thêm lại giá cho thuê cơ bản ban đầu
        if (lateDays >= thresholdDays)
        {
            penalty += basePricePerDay;
        }

        return penalty;
    }
}
