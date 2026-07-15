using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ClothingRentalUI.Data.Entities;

namespace ClothingRentalUI.Pages.Orders;

public partial class DetailModel
{
    private string BuildRentalPrintHtml(Order order, string shopName, string shopAddress, string shopPhone, string shopNotes)
    {
        var rentDateLocal = order.RentDate.AddHours(7);
        var dueDateLocal = order.DueDate.AddHours(7);

        // Grouping items based on Category
        var mainItems = order.OrderDetails
            .Where(od => !od.IsGift && !(od.Product?.Category?.Name?.Contains("phụ kiện", StringComparison.OrdinalIgnoreCase) == true || 
                                      od.Product?.Category?.Name?.Contains("accessory", StringComparison.OrdinalIgnoreCase) == true))
            .ToList();

        var paidAccessories = order.OrderDetails
            .Where(od => !od.IsGift && (od.Product?.Category?.Name?.Contains("phụ kiện", StringComparison.OrdinalIgnoreCase) == true || 
                                     od.Product?.Category?.Name?.Contains("accessory", StringComparison.OrdinalIgnoreCase) == true))
            .ToList();

        var freeAccessories = order.OrderDetails
            .Where(od => od.IsGift)
            .ToList();

        // 1. Table 2: Rental Clothing/Products
        var mainRowsHtml = new StringBuilder();
        for (int i = 0; i < mainItems.Count; i++)
        {
            var detail = mainItems[i];
            var siblings = mainItems.Where(od => od.ProductId == detail.ProductId).ToList();
            var suffix = siblings.Count > 1 ? $" (Chiếc #{siblings.IndexOf(detail) + 1})" : "";
            var prodName = (detail.Product?.Name ?? "Sản phẩm") + suffix;
            var size = detail.Product?.Size ?? "—";
            var color = detail.Product?.Color ?? "—";
            var condition = detail.ConditionAtReceive ?? detail.Product?.Condition ?? "Mới";
            var rentPriceStr = (detail.RentPrice * detail.RentDays).ToString("N0");

            mainRowsHtml.AppendLine($@"        <tr>
          <td>{i + 1}</td>
          <td>{detail.Product?.Code ?? "—"}</td>
          <td style=""text-align: left;"">{prodName}</td>
          <td>{size}</td>
          <td>{color}</td>
          <td style=""text-align: left;"">{condition}</td>
          <td>{rentPriceStr}</td>
        </tr>");
        }
        if (mainItems.Count == 0)
        {
            mainRowsHtml.AppendLine($@"        <tr>
          <td colspan=""7"" style=""text-align: center; color: #888;"">Không có sản phẩm thuê</td>
        </tr>");
        }

        // 2. Table 3: Free Accessories (grouped & dynamically split into 1 or 2 columns based on count)
        var freeGroups = freeAccessories
            .GroupBy(fa => fa.ProductId)
            .Select(g => new { Name = g.First().Product?.Name ?? "Phụ kiện", Qty = g.Count() })
            .ToList();

        string accessoriesWrapperHtml;
        bool hasRightTable = freeGroups.Count > 4;

        if (freeGroups.Count == 0)
        {
            accessoriesWrapperHtml = $@"    <div class=""accessories-wrapper"">
      <div class=""acc-table-half"">
        <table class=""acc-table"">
          <thead>
            <tr>
              <th style=""width: 15%;"">STT</th>
              <th style=""width: 60%;"">TÊN PHỤ KIỆN</th>
              <th style=""width: 25%;"">SL</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td colspan=""3"" style=""text-align: center; color: #888;"">Không có phụ kiện kèm theo</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>";
        }
        else if (!hasRightTable)
        {
            var leftAccHtml = new StringBuilder();
            for (int i = 0; i < freeGroups.Count; i++)
            {
                var group = freeGroups[i];
                leftAccHtml.AppendLine($@"            <tr>
              <td>{i + 1}</td>
              <td style=""text-align: left;"">{group.Name}</td>
              <td>{group.Qty}</td>
            </tr>");
            }

            accessoriesWrapperHtml = $@"    <div class=""accessories-wrapper"">
      <div class=""acc-table-half"">
        <table class=""acc-table"">
          <thead>
            <tr>
              <th style=""width: 15%;"">STT</th>
              <th style=""width: 60%;"">TÊN PHỤ KIỆN</th>
              <th style=""width: 25%;"">SL</th>
            </tr>
          </thead>
          <tbody>
{leftAccHtml}          </tbody>
        </table>
      </div>
    </div>";
        }
        else
        {
            var leftAccHtml = new StringBuilder();
            var rightAccHtml = new StringBuilder();
            int halfCount = (int)Math.Ceiling(freeGroups.Count / 2.0);
            for (int i = 0; i < halfCount; i++)
            {
                var group = freeGroups[i];
                leftAccHtml.AppendLine($@"            <tr>
              <td>{i + 1}</td>
              <td style=""text-align: left;"">{group.Name}</td>
              <td>{group.Qty}</td>
            </tr>");
            }

            for (int i = halfCount; i < freeGroups.Count; i++)
            {
                var group = freeGroups[i];
                rightAccHtml.AppendLine($@"            <tr>
              <td>{i + 1}</td>
              <td style=""text-align: left;"">{group.Name}</td>
              <td>{group.Qty}</td>
            </tr>");
            }

            accessoriesWrapperHtml = $@"    <div class=""accessories-wrapper"">
      <div class=""acc-table-half"">
        <table class=""acc-table"">
          <thead>
            <tr>
              <th style=""width: 15%;"">STT</th>
              <th style=""width: 60%;"">TÊN PHỤ KIỆN</th>
              <th style=""width: 25%;"">SL</th>
            </tr>
          </thead>
          <tbody>
{leftAccHtml}          </tbody>
        </table>
      </div>
      <div class=""acc-table-half"">
        <table class=""acc-table"">
          <thead>
            <tr>
              <th style=""width: 15%;"">STT</th>
              <th style=""width: 60%;"">TÊN PHỤ KIỆN</th>
              <th style=""width: 25%;"">SL</th>
            </tr>
          </thead>
          <tbody>
{rightAccHtml}          </tbody>
        </table>
      </div>
    </div>";
        }

        // 3. Table 4: Paid Accessories (grouped)
        var paidGroups = paidAccessories
            .GroupBy(pa => pa.ProductId)
            .Select(g => new { Name = g.First().Product?.Name ?? "Phụ kiện", Qty = g.Count(), RentPrice = g.First().RentPrice, RentDays = g.First().RentDays })
            .ToList();

        var paidRowsHtml = new StringBuilder();
        int maxPaidRows = Math.Max(3, paidGroups.Count);
        decimal totalPaidAccessories = 0;
        for (int i = 0; i < maxPaidRows; i++)
        {
            if (i < paidGroups.Count)
            {
                var group = paidGroups[i];
                var lineTotal = group.RentPrice * group.RentDays * group.Qty;
                totalPaidAccessories += lineTotal;

                paidRowsHtml.AppendLine($@"        <tr>
          <td>{i + 1}</td>
          <td style=""text-align: left;"">{group.Name} (x{group.RentDays} ngày)</td>
          <td>{group.Qty}</td>
          <td style=""text-align: right;"">{group.RentPrice.ToString("N0")}</td>
          <td style=""text-align: right;"">{lineTotal.ToString("N0")}</td>
        </tr>");
            }
            else
            {
                paidRowsHtml.AppendLine($@"        <tr class=""empty-row"">
          <td>{i + 1}</td>
          <td></td>
          <td></td>
          <td></td>
          <td></td>
        </tr>");
            }
        }

        // 4. Section 5: Deposit payment details
        var depositTx = order.Transactions?.FirstOrDefault(t => t.Type == "DEPOSIT_RECEIVED");
        bool isDepositCash = depositTx == null || depositTx.PaymentMethod.Equals("CASH", StringComparison.OrdinalIgnoreCase);
        bool isDepositTransfer = depositTx != null && (depositTx.PaymentMethod.Equals("TRANSFER", StringComparison.OrdinalIgnoreCase) || depositTx.PaymentMethod.Equals("BANK", StringComparison.OrdinalIgnoreCase));

        string depositCashChecked = isDepositCash ? "✓" : "";
        string depositTransferChecked = isDepositTransfer ? "✓" : "";

        // 5. Section 6: Payment summaries
        decimal totalMainRent = mainItems.Sum(od => od.RentPrice * od.RentDays);
        decimal finalAmount = order.TotalPrice - order.DiscountAmount;
        decimal totalToPay = finalAmount + order.TotalDeposit;
        
        string totalRentLabel = order.DiscountAmount > 0 
            ? $@"TỔNG CỘNG (Giảm giá voucher -{order.DiscountAmount.ToString("N0")}đ):" 
            : "TỔNG THANH TOÁN:";

        // 6. Section 7: Rental fee payment details
        var rentTx = order.Transactions?.FirstOrDefault(t => t.Type == "RENTAL_PAYMENT");
        bool isRentCash = rentTx == null || rentTx.PaymentMethod.Equals("CASH", StringComparison.OrdinalIgnoreCase);
        bool isRentTransfer = rentTx != null && (rentTx.PaymentMethod.Equals("TRANSFER", StringComparison.OrdinalIgnoreCase) || rentTx.PaymentMethod.Equals("BANK", StringComparison.OrdinalIgnoreCase));
        bool isRentOther = rentTx != null && !isRentCash && !isRentTransfer;
        string rentOtherVal = isRentOther ? (rentTx?.PaymentMethod ?? "") : "";

        string rentCashChecked = isRentCash ? "✓" : "";
        string rentTransferChecked = isRentTransfer ? "✓" : "";
        string rentOtherChecked = isRentOther ? "✓" : "";

        // 7. Section 8: Late fee settings
        string lateFeeDisplay = $"{LateFeePerDay.ToString("N0")}đ/ngày";

        return $@"<!DOCTYPE html>
<html lang=""vi"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>Phiếu Thuê Đồ - {order.Code}</title>
  <link rel=""preconnect"" href=""https://fonts.googleapis.com"">
  <link rel=""preconnect"" href=""https://fonts.gstatic.com"" crossorigin>
  <link href=""https://fonts.googleapis.com/css2?family=Be+Vietnam+Pro:wght@300;400;500;600;700&family=Caveat:wght@600&display=swap"" rel=""stylesheet"">
  <script src=""https://cdn.jsdelivr.net/npm/jsbarcode@3.11.5/dist/JsBarcode.all.min.js""></script>
  
  <style>
    :root {{
      --primary-dark: #5c3d2e;
      --primary-medium: #8c624e;
      --border-color: #dfd5ca;
      --bg-cream: #fefcf9;
      --text-color: #4a3329;
    }}

    * {{
      box-sizing: border-box;
      margin: 0;
      padding: 0;
    }}

    @page {{
      size: A5 portrait;
      margin: 0;
    }}

    body {{
      font-family: 'Be Vietnam Pro', sans-serif;
      background-color: #f0ebd9;
      color: var(--text-color);
      display: flex;
      justify-content: center;
      padding: 10px 5px;
      font-size: 10px;
    }}

    .form-wrapper {{
      background-color: var(--bg-cream);
      width: 100%;
      max-width: 560px;
      padding: 12px 15px;
      border-radius: 8px;
      box-shadow: 0 4px 15px rgba(0,0,0,0.06);
      position: relative;
      display: flex;
      flex-direction: column;
      gap: 5px;
    }}

    .header {{
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 2px;
    }}

    .logo-container {{
      display: flex;
      align-items: center;
    }}
    .logo-circle {{
      width: 50px;
      height: 50px;
      border: 1px solid var(--primary-dark);
      border-radius: 50%;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      position: relative;
    }}
    .logo-num {{
      font-family: 'Times New Roman', serif;
      font-size: 18px;
      line-height: 1;
      color: var(--primary-dark);
      letter-spacing: -1px;
    }}
    .logo-sub {{
      font-size: 7px;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      color: var(--primary-dark);
      margin-top: -1px;
    }}
    .logo-star {{
      position: absolute;
      top: 6px;
      right: 8px;
      font-size: 5px;
      color: var(--primary-dark);
    }}

    .title-area {{
      text-align: center;
      flex-grow: 1;
      padding: 0 5px;
    }}
    .title-area h1 {{
      font-size: 18px;
      font-weight: 700;
      color: var(--primary-dark);
      letter-spacing: 1px;
    }}
    .title-area p {{
      font-size: 8px;
      letter-spacing: 0.3px;
      margin-top: 1px;
      text-transform: uppercase;
      font-weight: 600;
    }}

    .meta-box {{
      border: 1px dotted var(--primary-medium);
      border-radius: 4px;
      padding: 4px 8px;
      width: 180px;
      font-size: 9.5px;
      display: flex;
      flex-direction: column;
      gap: 3px;
    }}
    .meta-row {{
      display: flex;
      align-items: flex-end;
    }}

    .dotted-line {{
      flex-grow: 1;
      border-bottom: 1.2px dotted var(--primary-medium);
      margin-left: 4px;
      height: 12px;
      padding-bottom: 1px;
      font-weight: 600;
      font-size: 10px;
    }}

    .banner-container {{
      display: flex;
      justify-content: center;
      margin: 0;
    }}
    .section-banner {{
      background-color: var(--primary-dark);
      color: white;
      padding: 2px 10px;
      border-radius: 50px;
      display: inline-flex;
      align-items: center;
      gap: 4px;
      font-size: 9px;
      text-transform: uppercase;
      font-weight: 700;
      letter-spacing: 0.3px;
    }}
    .section-banner svg,
    .section-banner-light svg {{
      width: 10px !important;
      height: 10px !important;
      min-width: 10px;
      flex-shrink: 0;
      fill: currentColor;
    }}

    .section-banner-light {{
      border: 1px solid var(--primary-dark);
      background-color: transparent;
      color: var(--primary-dark);
      padding: 2px 10px;
      border-radius: 50px;
      display: inline-flex;
      align-items: center;
      gap: 4px;
      font-size: 8.5px;
      text-transform: uppercase;
      font-weight: 700;
      letter-spacing: 0.3px;
    }}

    .form-grid {{
      display: flex;
      flex-direction: column;
      gap: 4px;
      padding: 1px 0;
    }}
    .form-row {{
      display: flex;
      gap: 10px;
    }}
    .input-group {{
      display: flex;
      align-items: flex-end;
    }}
    .input-label {{
      font-size: 10px;
      font-weight: 500;
      white-space: nowrap;
    }}

    table {{
      width: 100%;
      border-collapse: collapse;
      margin-top: 2px;
    }}
    th, td {{
      border: 1px solid var(--border-color);
      padding: 2px 4px;
      font-size: 9.5px;
      text-align: center;
    }}
    th {{
      background-color: var(--primary-dark);
      color: white;
      font-weight: 600;
      font-size: 8.5px;
      letter-spacing: 0.3px;
    }}
    .table-note {{
      font-size: 7.5px;
      font-weight: normal;
      display: block;
      margin-top: 1px;
      opacity: 0.9;
    }}
    .empty-row {{
      height: 16px;
    }}
    
    .table-footer-sum {{
      display: flex;
      justify-content: flex-end;
      align-items: flex-end;
      margin-top: 3px;
      font-size: 10.5px;
      font-weight: 600;
    }}

    .accessories-wrapper {{
      display: flex;
      gap: 8px;
    }}
    .acc-table-half {{
      flex: 1;
    }}
    .acc-table th {{
      background-color: #f5efe6;
      color: var(--primary-dark);
      font-weight: 700;
    }}

    .payment-grid {{
      display: grid;
      grid-template-columns: 1fr 1.1fr 1fr;
      border: 1.2px solid var(--border-color);
      border-radius: 4px;
      margin-top: 2px;
    }}
    .payment-col {{
      padding: 4px 6px;
      display: flex;
      flex-direction: column;
      gap: 4px;
    }}
    .payment-col:not(:last-child) {{
      border-right: 1.2px solid var(--border-color);
    }}
    .col-title {{
      font-size: 9.5px;
      font-weight: 700;
      display: flex;
      align-items: center;
      gap: 3px;
    }}
    .col-title svg {{
      width: 10px;
      height: 10px;
      fill: var(--primary-dark);
    }}

    .checkbox-group {{
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      font-size: 9.5px;
    }}
    .checkbox-item {{
      display: inline-flex;
      align-items: center;
      gap: 3px;
    }}
    .box {{
      width: 10px;
      height: 10px;
      border: 1px solid var(--primary-dark);
      border-radius: 2px;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      font-weight: bold;
      font-size: 7.5px;
    }}

    .return-section {{
      border-top: 1px solid var(--border-color);
      padding-top: 4px;
      margin-top: 0;
    }}
    .return-subtext {{
      font-size: 8px;
      font-style: italic;
      color: var(--primary-medium);
      margin-top: 2px;
      text-align: right;
    }}

    .signatures-row {{
      display: grid;
      grid-template-columns: 1fr 1fr 1fr;
      text-align: center;
      margin-top: 6px;
      margin-bottom: 2px;
    }}
    .sig-title {{
      font-size: 10px;
      font-weight: 700;
    }}
    .sig-subtitle {{
      font-size: 8px;
      font-weight: normal;
      display: block;
      margin-top: 1px;
    }}
    .thank-you {{
      font-family: 'Caveat', cursive;
      font-size: 16px;
      color: var(--primary-medium);
      display: flex;
      align-items: center;
      justify-content: center;
      height: 100%;
    }}
    .sig-space {{
      height: 22px;
    }}

    .footer-bar {{
      background-color: var(--primary-dark);
      color: white;
      margin: 0 -15px -12px -15px;
      padding: 4px 10px;
      border-bottom-left-radius: 8px;
      border-bottom-right-radius: 8px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 8.5px;
    }}
    .footer-item {{
      display: flex;
      align-items: center;
      gap: 3px;
    }}
    .footer-item svg {{
      width: 9px;
      height: 9px;
      fill: currentColor;
    }}

    @media print {{
      @page {{
        size: A5 portrait;
        margin: 0;
      }}
      body {{
        display: block !important;
        background-color: white;
        padding: 5mm 6mm 5mm 6mm;
        margin: 0;
        width: 148mm;
        height: 210mm;
        box-sizing: border-box;
        position: relative;
      }}
      .form-wrapper {{
        display: flex;
        flex-direction: column;
        box-sizing: border-box;
        box-shadow: none;
        width: 100%;
        height: 100%;
        padding: 0 0 25px 0 !important;
        margin: 0;
        border-radius: 0;
        background-color: white;
        position: relative;
        gap: 4px;
      }}
      .footer-bar {{
        position: absolute !important;
        bottom: 0;
        left: 0;
        right: 0;
        margin: 0 !important;
        border-radius: 0 !important;
        padding: 4px 8px;
      }}
    }}
  </style>
</head>
<body>

  <div class=""form-wrapper"">
    
    <!-- HEADER -->
    <div class=""header"">
      <div class=""logo-container"">
        <div class=""logo-circle"">
          <span class=""logo-num"">9495</span>
          <span class=""logo-sub"">by COMI</span>
          <span class=""logo-star"">✦</span>
        </div>
      </div>
      <div class=""title-area"">
        <h1>PHIẾU THUÊ ĐỒ</h1>
        <p>♥ {shopNotes} ♥</p>
      </div>
      <div class=""meta-box"">
        <div class=""meta-row"">SỐ PHIẾU: <span class=""dotted-line"" style=""font-family: monospace; font-size: 13px;"">{order.Code}</span></div>
        <div class=""meta-row"">Ngày thuê: <span class=""dotted-line"" style=""text-align: center;"">{rentDateLocal.ToString("dd")} / {rentDateLocal.ToString("MM")} / {rentDateLocal.ToString("yyyy")}</span></div>
        <div class=""meta-row"">Giờ thuê: <span class=""dotted-line"" style=""text-align: center;"">{rentDateLocal.ToString("HH:mm")}</span></div>
        <div style=""text-align: center; margin-top: 4px; display: flex; justify-content: center; align-items: center; height: 25px;"">
          <svg id=""order-barcode""></svg>
        </div>
      </div>
    </div>

    <!-- 1. THÔNG TIN KHÁCH HÀNG -->
    <div class=""banner-container"">
      <div class=""section-banner"">
        <svg viewBox=""0 0 24 24""><path d=""M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z""/></svg>
        1. Thông tin khách hàng
      </div>
    </div>
    <div class=""form-grid"">
      <div class=""form-row"">
        <div class=""input-group"" style=""flex: 65;"">
          <span class=""input-label"">Họ và tên:</span>
          <span class=""dotted-line"">{order.Customer?.FullName}</span>
        </div>
        <div class=""input-group"" style=""flex: 35;"">
          <span class=""input-label"">SĐT:</span>
          <span class=""dotted-line"">{order.Customer?.PhoneNumber}</span>
        </div>
      </div>
      <div class=""form-row"">
        <div class=""input-group"" style=""flex: 40;"">
          <span class=""input-label"">CMND/CCCD:</span>
          <span class=""dotted-line"">{order.Customer?.IdentityCard ?? "—"}</span>
        </div>
        <div class=""input-group"" style=""flex: 30;"">
          <span class=""input-label"">Ngày cấp:</span>
          <span class=""dotted-line""></span>
        </div>
        <div class=""input-group"" style=""flex: 30;"">
          <span class=""input-label"">Nơi cấp:</span>
          <span class=""dotted-line""></span>
        </div>
      </div>
    </div>

    <!-- 2. THÔNG TIN SẢN PHẨM THUÊ -->
    <div class=""banner-container"">
      <div class=""section-banner"">
        <svg viewBox=""0 0 24 24""><path d=""M12 2a3 3 0 0 1 3 3c0 .9-.4 1.7-1 2.2V8l7 5v2H3v-2l7-5v-.8c-.6-.5-1-1.3-1-2.2a3 3 0 0 1 3-3zm0 2a1 1 0 0 0-1 1c0 .6.4 1 1 1s1-.4 1-1-.4-1-1-1zm-7 9 7-5 7 5H5z""/></svg>
        2. Thông tin sản phẩm thuê
      </div>
    </div>
    <table>
      <thead>
        <tr>
          <th style=""width: 6%;"">STT</th>
          <th style=""width: 14%;"">MÃ ĐỒ</th>
          <th style=""width: 28%;"">TÊN SẢN PHẨM</th>
          <th style=""width: 8%;"">SIZE</th>
          <th style=""width: 8%;"">MÀU</th>
          <th style=""width: 24%;"">TÌNH TRẠNG KHI NHẬN <span class=""table-note"">(Ghi chú)</span></th>
          <th style=""width: 12%;"">GIÁ THUÊ <span class=""table-note"">(đ)</span></th>
        </tr>
      </thead>
      <tbody>
{mainRowsHtml}      </tbody>
    </table>
    <div class=""table-footer-sum"" style=""margin-top: -5px;"">
      <span class=""input-label"">TỔNG TIỀN THUÊ SẢN PHẨM:</span>
      <span class=""dotted-line"" style=""max-width: 240px; text-align: right; padding-right: 5px;"">{totalMainRent.ToString("N0")}</span><span style=""padding-left: 5px;"">đ</span>
    </div>

    <!-- 3. PHỤ KIỆN ĐI KÈM (MIỄN PHÍ) -->
    <div class=""banner-container"">
      <div class=""section-banner-light"">
        <svg viewBox=""0 0 24 24""><path d=""M20 6h-2.18c.11-.31.18-.65.18-1a2.5 2.5 0 0 0-5-0.5c0 .17.02.33.05.5H12c.03-.17.05-.33.05-.5A2.5 2.5 0 0 0 7 4.5c0 .35.07.69.18 1H5a2 2 0 0 0-2 2v3a1 1 0 0 0 1 1h1v10a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V12h1a1 1 0 0 0 1-1V7a2 2 0 0 0-2-2zM9.5 5A1.5 1.5 0 0 1 11 6.5V7H9.5A1.5 1.5 0 0 1 8 5.5c0-.83.67-1.5 1.5-1.5zm5 0A1.5 1.5 0 0 1 16 5.5c0 .83-.67 1.5-1.5 1.5H13v-.5A1.5 1.5 0 0 1 14.5 5zM5 8h14v2H5V8zm2 4h4v9H7v-9zm10 9h-4v-9h4v9z""/></svg>
        3. Phụ kiện đi kèm (Miễn phí)
      </div>
    </div>
    {accessoriesWrapperHtml}

    <!-- SECTION 4, 5, 6: TIỀN CỌC & THANH TOÁN -->
    <div class=""payment-grid"">
      <!-- Cột 4 -->
      <div class=""payment-col"">
        <div class=""col-title"">
          <svg viewBox=""0 0 24 24""><path d=""M12 1L3 5v6c0 5.5 3.8 10.7 9 12 5.2-1.3 9-6.5 9-12V5l-9-4zm-2 15l-4-4 1.4-1.4L10 13.2l6.6-6.6L18 8l-8 8z""/></svg>
          4. TIỀN CỌC (TK2)
        </div>
        <div class=""input-group"" style=""width: 100%;"">
          <span class=""input-label"">Số tiền cọc:</span>
          <span class=""dotted-line"" style=""text-align: right; padding-right: 5px;"">{order.TotalDeposit.ToString("N0")}</span><span style=""padding-left: 5px; font-size: 12px;"">đ</span>
        </div>
        <div class=""checkbox-group"" style=""margin-top: 5px;"">
          <span class=""input-label"">Hình thức:</span>
          <label class=""checkbox-item""><span class=""box"">{depositCashChecked}</span> Tiền mặt</label>
          <label class=""checkbox-item""><span class=""box"">{depositTransferChecked}</span> Chuyển khoản</label>
        </div>
      </div>
      
      <!-- Cột 5 -->
      <div class=""payment-col"">
        <div class=""col-title"">
          <svg viewBox=""0 0 24 24""><path d=""M19 19V5H5v14h14zm0-16c1.1 0 2 .9 2 2v14c0 1.1-.9 2-2 2H5c-1.1 0-2-.9-2-2V5c0-1.1.9-2 2-2h14zm-4 4h2v2h-2V7zm-4 0h2v2h-2V7zm-4 0h2v2H7V7zm0 4h2v2H7v-2zm4 0h2v2h-2v-2zm4 0h2v2h-2v-2zm-8 4h10v2H7v-2z""/></svg>
          5. TỔNG THANH TOÁN KHI THUÊ
        </div>
        <div class=""input-group"" style=""width: 100%;"">
          <span class=""input-label"" style=""font-size: 11px;"">Tổng tiền thuê:</span>
          <span class=""dotted-line"" style=""text-align: right; padding-right: 5px;"">{finalAmount.ToString("N0")}</span><span style=""padding-left: 5px; font-size: 11px;"">đ</span>
        </div>
        <div class=""input-group"" style=""width: 100%; margin-top: 3px;"">
          <span class=""input-label"" style=""font-size: 11px;"">Tổng tiền cọc:</span>
          <span class=""dotted-line"" style=""text-align: right; padding-right: 5px;"">{order.TotalDeposit.ToString("N0")}</span><span style=""padding-left: 5px; font-size: 11px;"">đ</span>
        </div>
        <div class=""input-group"" style=""width: 100%; margin-top: 5px;"">
          <span class=""input-label"" style=""font-weight: 700; font-size: 11px;"">TỔNG CỘNG:</span>
          <span class=""dotted-line"" style=""text-align: right; padding-right: 5px; font-weight: 700;"">{totalToPay.ToString("N0")}</span><span style=""padding-left: 5px; font-size: 11px; font-weight: 700;"">đ</span>
        </div>
      </div>
      
      <!-- Cột 6 -->
      <div class=""payment-col"">
        <div class=""col-title"">6. HÌNH THỨC THANH TOÁN</div>
        <div style=""display: flex; flex-direction: column; gap: 8px; margin-top: 5px;"">
          <label class=""checkbox-item""><span class=""box"">{rentCashChecked}</span> Tiền mặt</label>
          <label class=""checkbox-item""><span class=""box"">{rentTransferChecked}</span> Chuyển khoản</label>
          <div class=""input-group"" style=""width: 100%;"">
            <label class=""checkbox-item""><span class=""box"">{rentOtherChecked}</span> Khác:</label>
            <span class=""dotted-line"" style=""font-size: 11px;"">{rentOtherVal}</span>
          </div>
        </div>
      </div>
    </div>

    <!-- 7. THỜI GIAN HẸN TRẢ -->
    <div class=""return-section"">
      <div class=""col-title"" style=""margin-bottom: 10px;"">
        <svg viewBox=""0 0 24 24""><path d=""M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8zm.5-13H11v6l5.25 3.15.75-1.23-4.5-2.67z""/></svg>
        7. THỜI GIAN HẸN TRẢ
      </div>
      <div class=""form-row"">
        <div class=""input-group"" style=""flex: 33;"">
          <span class=""input-label"">Ngày hẹn trả:</span>
          <span class=""dotted-line"" style=""text-align: center;"">{dueDateLocal.ToString("dd")} / {dueDateLocal.ToString("MM")} / {dueDateLocal.ToString("yyyy")}</span>
        </div>
        <div class=""input-group"" style=""flex: 27;"">
          <span class=""input-label"">Giờ hẹn trả:</span>
          <span class=""dotted-line"" style=""text-align: center;""></span>
        </div>
        <div class=""input-group"" style=""flex: 40;"">
          <span class=""input-label"">Phí trễ hạn (nếu có):</span>
          <span class=""dotted-line"" style=""text-align: center;"">{lateFeeDisplay}</span>
        </div>
      </div>
      <div class=""return-subtext"">(Phí trễ hạn tính theo quy định của 9495 by COMI)</div>
    </div>

    <!-- CHỮ KÝ -->
    <div class=""signatures-row"">
      <div>
        <span class=""sig-title"">KHÁCH HÀNG</span>
        <span class=""sig-subtitle"">(Ký & ghi rõ họ tên)</span>
        <div class=""sig-space""></div>
        <span class=""dotted-line"" style=""display: inline-block; width: 85%;"">{order.Customer?.FullName}</span>
      </div>
      <div class=""thank-you"">
        Thank you! ♡
      </div>
      <div>
        <span class=""sig-title"">NHÂN VIÊN</span>
        <span class=""sig-subtitle"">(Ký & ghi rõ họ tên)</span>
        <div class=""sig-space""></div>
        <span class=""dotted-line"" style=""display: inline-block; width: 85%;"">{order.CreatedByUser?.FullName ?? "Cửa hàng"}</span>
      </div>
    </div>

    <!-- BANNER CHÂN TRANG -->
    <div class=""footer-bar"">
      <div class=""footer-item"">
        <svg viewBox=""0 0 24 24""><path d=""M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5a2.5 2.5 0 0 1 0-5 2.5 2.5 0 0 1 0 5z""/></svg>
        {shopAddress}
      </div>
      <div class=""footer-item"">
        <svg viewBox=""0 0 24 24""><path d=""M6.62 10.79a15.15 15.15 0 0 0 6.59 6.59l2.2-2.2c.27-.27.67-.36 1.02-.24 1.12.37 2.33.57 3.57.57.55 0 1 .45 1 1V20c0 .55-.45 1-1 1-9.39 0-17-7.61-17-17 0-.55.45-1 1-1h3.5c.55 0 1 .45 1 1 0 1.25.2 2.45.57 3.57.11.35.03.74-.25 1.02l-2.2 2.2z""/></svg>
        {shopPhone}
      </div>
      <div class=""footer-item"">
        <svg viewBox=""0 0 24 24""><path d=""M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z""/></svg>
        9495 BY COMI - FOR YOU
      </div>
    </div>

  </div>

  <script>
    window.onload = function() {{
      try {{
        JsBarcode(""#order-barcode"", ""{order.Code}"", {{
          format: ""CODE128"",
          width: 1.2,
          height: 25,
          displayValue: false,
          margin: 0
        }});
      }} catch(e) {{
        console.error(e);
      }}
      
      const urlParams = new URLSearchParams(window.location.search);
      if (!urlParams.has('noprint')) {{
        setTimeout(function() {{
          window.print();
          window.onafterprint = function() {{
            window.close();
          }};
        }}, 300);
      }}
    }};
  </script>
</body>
</html>";
    }

    private string BuildInvoicePrintHtml(Order order, string shopName, string shopAddress, string shopPhone, string shopNotes, string printWidth)
    {
        var title = "HÓA ĐƠN THANH TOÁN";
        var totalPayment = order.TotalPrice - order.DiscountAmount + order.TotalDeposit + order.TotalPenalty;

        var html = $@"<!DOCTYPE html>
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
            @page {{
                margin: 0;
            }}
            body {{
                width: 100%;
                padding: 2mm 4mm;
                margin: 0;
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
            <td class=""info-label"">Ngày thuê:</td>
            <td>{order.RentDate.AddHours(7).ToString("dd/MM/yyyy HH:mm")}</td>
        </tr>
        <tr>
            <td class=""info-label"">Hạn trả đồ:</td>
            <td>{order.DueDate.AddHours(7).ToString("dd/MM/yyyy")}</td>
        </tr>
        <tr>
            <td class=""info-label"">Giữ giấy tờ:</td>
            <td><strong>{(order.IsIdCardReceived ? "Đã nhận CCCD" : "Không nhận (Cọc thêm)")}</strong></td>
        </tr>
    </table>
    
    <table class=""items-table"">
        <thead>
            <tr>
                <th>Sản phẩm</th>
                <th style=""text-align: right;"">Thuê/Cọc</th>
                <th style=""text-align: right;"">T.Tiền</th>
            </tr>
        </thead>
        <tbody>";

        var printDetailsGrouped = order.OrderDetails.GroupBy(od => od.ProductId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var detail in order.OrderDetails)
        {
            var siblings = printDetailsGrouped[detail.ProductId];
            var unitIndex = siblings.IndexOf(detail) + 1;
            var suffix = siblings.Count > 1 ? $" (Chiếc #{unitIndex})" : "";
            var prodName = (detail.Product?.Name ?? "Sản phẩm") + suffix;
            var sizeColor = $"({detail.Product?.Size ?? "—"}/{detail.Product?.Color ?? "—"})";
            var rentInfo = $"{detail.RentPrice.ToString("N0")}₫ x {detail.RentDays} ngày";
            var depositInfo = $"Cọc: {detail.Deposit.ToString("N0")}₫";
            var detailTotal = (detail.RentPrice * detail.RentDays).ToString("N0") + "₫";
            
            html += $@"
            <tr>
                <td>
                    <div>{prodName}</div>
                    <div style=""font-size: 9px; color: #666;"">{sizeColor}</div>
                </td>
                <td style=""text-align: right; font-size: 10px;"">
                    <div>{rentInfo}</div>
                    <div>{depositInfo}</div>
                </td>
                <td style=""text-align: right; font-weight: bold; vertical-align: middle;"">{detailTotal}</td>
            </tr>";
        }

        html += $@"
        </tbody>
    </table>
    
    <div class=""summary-section"">
        <div class=""summary-row"">
            <span>Tiền thuê đồ:</span>
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
        <div class=""summary-row"">
            <span>Tiền đặt cọc:</span>
            <span>{order.TotalDeposit.ToString("N0")}₫</span>
        </div>";

        if (order.TotalPenalty > 0)
        {
            html += $@"
        <div class=""summary-row"" style=""color: red;"">
            <span>Phí phát sinh:</span>
            <span>+{order.TotalPenalty.ToString("N0")}₫</span>
        </div>";
        }

        html += $@"
        <div class=""summary-row total"">
            <span>TỔNG CỘNG:</span>
            <span>{totalPayment.ToString("N0")}₫</span>
        </div>
    </div>
    
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

        return html;
    }
}
