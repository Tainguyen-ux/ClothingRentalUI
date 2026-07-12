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

namespace ClothingRentalUI.Pages.Reports;

public class IdCardsModel : PageModel
{
    private readonly ClothingRentalDbContext _context;

    public IdCardsModel(ClothingRentalDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SearchTerm { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;

    public int TotalPages { get; set; }
    public int TotalItems { get; set; }
    public int PageSize => 20;

    public List<Order> OrdersData { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var username = HttpContext.Session.GetString("Username");
        if (string.IsNullOrEmpty(username))
        {
            return RedirectToPage("/Auth/Login");
        }

        var hasPermission = await _context.Users
            .Include(u => u.UserPermissions)
                .ThenInclude(up => up.Permission)
            .AnyAsync(u => u.Username.ToLower() == username.ToLower() && 
                      u.UserPermissions.Any(up => up.Permission != null && up.Permission.Code == "REPORT_ID_CARDS"));

        if (!hasPermission)
        {
            return RedirectToPage("/Reports/Index");
        }

        // Ngày mặc định: Từ đầu tháng đến ngày hiện tại
        var todayVn = DateTime.UtcNow.AddHours(7).Date;
        if (FromDate == null) FromDate = new DateTime(todayVn.Year, todayVn.Month, 1);
        if (ToDate == null) ToDate = todayVn;

        var startUtc = DateTime.SpecifyKind(FromDate.Value.Date.AddHours(-7), DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(ToDate.Value.Date.AddDays(1).AddHours(-7), DateTimeKind.Utc);

        var query = _context.Orders
            .Include(o => o.Customer)
            .Where(o => o.CreatedAt >= startUtc && o.CreatedAt < endUtc && 
                       ((o.Customer != null && o.Customer.IdentityCard != null && o.Customer.IdentityCard != "") || o.IsIdCardReceived));

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var clean = SearchTerm.Trim().ToLower();
            query = query.Where(o => (o.Customer != null && o.Customer.FullName.ToLower().Contains(clean)) ||
                                     (o.Customer != null && o.Customer.PhoneNumber.Contains(clean)) ||
                                     (o.Customer != null && o.Customer.IdentityCard != null && o.Customer.IdentityCard.Contains(clean)) ||
                                     o.Code.ToLower().Contains(clean));
        }

        var allFiltered = await query
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();

        // Phân trang
        TotalItems = allFiltered.Count;
        TotalPages = (int)Math.Ceiling(TotalItems / (double)PageSize);
        if (PageIndex < 1) PageIndex = 1;
        if (TotalPages > 0 && PageIndex > TotalPages) PageIndex = TotalPages;

        OrdersData = allFiltered
            .Skip((PageIndex - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        return Page();
    }
}
