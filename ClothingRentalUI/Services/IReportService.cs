using System;
using System.Threading.Tasks;
using ClothingRentalUI.Models.Common;
using ClothingRentalUI.Models.Report;

namespace ClothingRentalUI.Services;

public interface IReportService
{
    Task<ServiceResult<ReportSummaryDto>> GetReportSummaryAsync(DateTime fromDate, DateTime toDate, int lowStockThreshold);
}

