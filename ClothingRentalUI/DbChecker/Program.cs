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
            cmd.CommandText = "INSERT INTO \"Menus\" (\"Name\", \"Url\", \"Icon\", \"ParentId\", \"DisplayOrder\", \"RequiredPermissionId\") VALUES ('Lịch sử nhập hàng', '/Products/ImportHistory', 'fas fa-history', 2, 5, (SELECT \"Id\" FROM \"Permissions\" WHERE \"Code\" = 'CLOTHES_IMPORT_HISTORY'));";
            db.Database.OpenConnection();
            var res = cmd.ExecuteNonQuery();
            Console.WriteLine($"Inserted rows: {res}");
        }
    }
}
