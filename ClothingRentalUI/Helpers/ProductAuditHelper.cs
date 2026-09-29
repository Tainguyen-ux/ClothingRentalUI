using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ClothingRentalUI.Data.Entities;
using ClothingRentalUI.Models.Clothes;

namespace ClothingRentalUI.Helpers;

public static class ProductAuditHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static List<ProductAuditLogEntry> ParseLogs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<ProductAuditLogEntry>();
        try
        {
            return JsonSerializer.Deserialize<List<ProductAuditLogEntry>>(json, JsonOptions) ?? new List<ProductAuditLogEntry>();
        }
        catch
        {
            return new List<ProductAuditLogEntry>();
        }
    }

    public static string SerializeLogs(List<ProductAuditLogEntry> logs)
    {
        return JsonSerializer.Serialize(logs, JsonOptions);
    }

    public static void AppendLog(Product product, ProductAuditLogEntry entry)
    {
        var logs = ParseLogs(product.SystemLog);
        logs.Add(entry);
        product.SystemLog = SerializeLogs(logs);
    }

    public static string FormatValue(object? val)
    {
        if (val == null) return "Trống";
        var s = val.ToString()?.Trim();
        return string.IsNullOrEmpty(s) ? "Trống" : s;
    }

    public static string FormatCurrency(decimal val)
    {
        return val.ToString("N0") + "đ";
    }
}
