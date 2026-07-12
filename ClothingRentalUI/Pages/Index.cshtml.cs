using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;

namespace ClothingRentalUI.Pages;

public class IndexModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public IndexModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    public bool IsLoggedIn { get; set; }
    public string UserFullName { get; set; } = string.Empty;

    public List<Order> HandoverToday { get; set; } = new();
    public List<Order> ReturnToday { get; set; } = new();
    public List<Order> OverdueRentals { get; set; } = new();
    public List<Order> Next7DaysSchedule { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToPage("/Auth/Login");
        }
        IsLoggedIn = true;
        UserFullName = HttpContext.Session.GetString("FullName") ?? string.Empty;

        // Current time in Vietnam (UTC+7)
        DateTime vnNow = DateTime.UtcNow.AddHours(7);
        DateTime todayStartVn = vnNow.Date;
        DateTime todayEndVn = todayStartVn.AddDays(1);
        
        DateTime todayStartUtc = DateTime.SpecifyKind(todayStartVn, DateTimeKind.Utc);
        DateTime todayEndUtc = DateTime.SpecifyKind(todayEndVn, DateTimeKind.Utc);
        DateTime sevenDaysLaterUtc = DateTime.SpecifyKind(todayStartVn.AddDays(8), DateTimeKind.Utc);

        // Fetch orders for coordination
        // 1. Handover Today: Draft orders with RentDate <= todayStartUtc (including past draft orders)
        HandoverToday = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderDetails).ThenInclude(od => od.Product)
            .Where(o => o.OrderType == "Rental" && o.Status == "Draft" && o.RentDate <= todayStartUtc)
            .OrderBy(o => o.RentDate)
            .ToListAsync();

        // 2. Return Today: Rented/PartiallyReturned orders with DueDate == todayStartUtc
        ReturnToday = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderDetails).ThenInclude(od => od.Product)
            .Where(o => o.OrderType == "Rental" && (o.Status == "Rented" || o.Status == "PartiallyReturned") && o.DueDate == todayStartUtc)
            .OrderBy(o => o.DueDate)
            .ToListAsync();

        // 3. Overdue: Rented/PartiallyReturned orders with DueDate < todayStartUtc
        OverdueRentals = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderDetails).ThenInclude(od => od.Product)
            .Where(o => o.OrderType == "Rental" && (o.Status == "Rented" || o.Status == "PartiallyReturned") && o.DueDate < todayStartUtc)
            .OrderBy(o => o.DueDate)
            .ToListAsync();

        // 4. Next 7 Days: Orders (Draft, Rented, PartiallyReturned) scheduled between tomorrow and 7 days later
        var tomorrowUtc = todayStartUtc.AddDays(1);
        Next7DaysSchedule = await _context.Orders
            .Include(o => o.Customer)
            .Include(o => o.OrderDetails).ThenInclude(od => od.Product)
            .Where(o => o.OrderType == "Rental" && (o.Status == "Draft" || o.Status == "Rented" || o.Status == "PartiallyReturned") && 
                    ((o.Status == "Draft" && o.RentDate >= tomorrowUtc && o.RentDate <= sevenDaysLaterUtc) || 
                     ((o.Status == "Rented" || o.Status == "PartiallyReturned") && o.DueDate >= tomorrowUtc && o.DueDate <= sevenDaysLaterUtc)))
            .OrderBy(o => o.Status == "Draft" ? o.RentDate : o.DueDate)
            .ToListAsync();

        return Page();
    }
}
