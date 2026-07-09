using System;
using System.Globalization;

namespace ClothingRentalUI.Helpers;

public static class FormatHelper
{
    private static readonly CultureInfo VietCulture = new CultureInfo("vi-VN");

    public static string FormatCurrency(decimal amount)
    {
        return amount.ToString("#,##0", VietCulture) + " đ";
    }

    public static string FormatDate(DateTime date)
    {
        return date.ToString("dd/MM/yyyy");
    }

    public static string FormatDateTime(DateTime date)
    {
        return date.ToString("dd/MM/yyyy HH:mm");
    }
}
