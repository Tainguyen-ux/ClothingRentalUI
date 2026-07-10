
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using ClothingRentalUI.Data;
using ClothingRentalUI.Data.Entities;
using Microsoft.Extensions.Configuration;
namespace Checker {
    class Program {
        static void Main() {
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            var config = builder.Build();
            var optionsBuilder = new DbContextOptionsBuilder<ClothingRentalDbContext>();
            optionsBuilder.UseNpgsql(config.GetConnectionString("DefaultConnection"));
            
            using var db = new ClothingRentalDbContext(optionsBuilder.Options);
            
            var menus = db.Menus.ToList();
            foreach (var m in menus) {
                Console.WriteLine($"Menu: ID={m.Id}, Name=\"{m.Name}\", Url=\"{m.Url}\", ParentId={m.ParentId}");
            }
        }
    }
}
