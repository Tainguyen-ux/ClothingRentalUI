
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Checker {
    class Program {
        static void Main() {
            var builder = new ConfigurationBuilder().AddJsonFile(@"d:\WebApp\ClothingRentalUI\ClothingRentalUI\appsettings.json");
            var config = builder.Build();
            var optionsBuilder = new DbContextOptionsBuilder();
            optionsBuilder.UseNpgsql(config.GetConnectionString("DefaultConnection"));
            
            using var db = new DbContext(optionsBuilder.Options);
            
            using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "UPDATE \"Menus\" SET \"Icon\" = '??' WHERE \"Url\" = '/Products/ImportHistory';";
            db.Database.OpenConnection();
            var res = cmd.ExecuteNonQuery();
            Console.WriteLine($"Updated rows: {res}");
        }
    }
}
