using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ClothingRentalUI.Data;

namespace ClothingRentalUI;

public static class RestoreHelper
{
    public static async Task RunAsync(string[] args)
    {
        if (!args.Contains("--restore-db"))
        {
            return;
        }

        Console.WriteLine("=== CHẠY MIGRATION RESTORE DATABASE TỪ DÒNG LỆNH ===");
        var connectionString = "Host=163.61.73.83;Port=5432;Database=ClothingRental;Username=postgres;Password=123123@";
        var optionsBuilder = new DbContextOptionsBuilder<ClothingRentalDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        
        using var db = new ClothingRentalDbContext(optionsBuilder.Options);
        
        string srcConnStr = "Host=shuttle.proxy.rlwy.net;Port=13850;Database=ClothingRental;Username=postgres;Password=JTEACZWktwZpECtLpznZpGdOdqHylHVh";
        string destConnStr = connectionString;

        var tables = new[]
        {
            "Permissions",
            "Users",
            "UserPermissions",
            "Categories",
            "PriceLists",
            "Products",
            "Customers",
            "Vouchers",
            "Orders",
            "OrderDetails",
            "Transactions",
            "Menus",
            "SystemSettings",
            "StockHistories",
            "ProductAttributes"
        };

        try
        {
            Console.WriteLine("Đang khởi tạo cơ sở dữ liệu trên VPS nếu chưa tồn tại (EnsureCreated)...");
            await db.Database.EnsureCreatedAsync();
            Console.WriteLine("Cơ sở dữ liệu VPS đã sẵn sàng.");

            using var srcConn = new NpgsqlConnection(srcConnStr);
            using var destConn = new NpgsqlConnection(destConnStr);

            await srcConn.OpenAsync();
            await destConn.OpenAsync();

            Console.WriteLine("Đã mở kết nối thành công tới cả hai cơ sở dữ liệu.");

            using (var cmd = new NpgsqlCommand("SET session_replication_role = 'replica';", destConn))
            {
                await cmd.ExecuteNonQueryAsync();
            }
            Console.WriteLine("Đã tạm thời tắt các ràng buộc khóa ngoại (replica mode) trên VPS.");

            foreach (var table in tables)
            {
                Console.WriteLine($"Đang xóa dữ liệu cũ trong bảng \"{table}\" trên VPS...");
                using var cmd = new NpgsqlCommand($"TRUNCATE TABLE \"{table}\" CASCADE;", destConn);
                await cmd.ExecuteNonQueryAsync();
            }

            foreach (var table in tables)
            {
                Console.WriteLine($"Đang sao chép bảng \"{table}\"...");
                
                // Get destination columns
                var destColumns = new List<string>();
                using (var colCmd = new NpgsqlCommand($@"
                    SELECT column_name 
                    FROM information_schema.columns 
                    WHERE table_name = '{table}';", destConn))
                {
                    using var colReader = await colCmd.ExecuteReaderAsync();
                    while (await colReader.ReadAsync())
                    {
                        destColumns.Add(colReader.GetString(0));
                    }
                }

                using var selectCmd = new NpgsqlCommand($"SELECT * FROM \"{table}\";", srcConn);
                using var reader = await selectCmd.ExecuteReaderAsync();

                var dt = new DataTable();
                dt.Load(reader);
                reader.Close();

                Console.WriteLine($"Đọc được {dt.Rows.Count} bản ghi từ Railway.");

                // Only keep columns that exist in both source and destination
                var commonColumns = new List<string>();
                foreach (DataColumn col in dt.Columns)
                {
                    if (destColumns.Contains(col.ColumnName, StringComparer.OrdinalIgnoreCase))
                    {
                        commonColumns.Add(col.ColumnName);
                    }
                }

                if (dt.Rows.Count > 0 && commonColumns.Count > 0)
                {
                    var columnsEscaped = commonColumns.Select(c => $"\"{c}\"").ToList();
                    var columnsStr = string.Join(", ", columnsEscaped);

                    int batchSize = 200;
                    for (int i = 0; i < dt.Rows.Count; i += batchSize)
                    {
                        var commandParameters = new List<NpgsqlParameter>();
                        var rowsQuery = new List<string>();

                        int end = Math.Min(i + batchSize, dt.Rows.Count);
                        for (int j = i; j < end; j++)
                        {
                            var rowParams = new List<string>();
                            for (int k = 0; k < commonColumns.Count; k++)
                            {
                                string paramName = $"@p_{j}_{k}";
                                rowParams.Add(paramName);
                                var colName = commonColumns[k];
                                var val = dt.Rows[j][colName];
                                var param = new NpgsqlParameter(paramName, val ?? DBNull.Value);
                                if (colName == "SystemLog" || colName == "DynamicAttributes" || colName == "ValueJson")
                                {
                                    param.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
                                }
                                commandParameters.Add(param);
                            }
                            rowsQuery.Add($"({string.Join(", ", rowParams)})");
                        }

                        string query = $"INSERT INTO \"{table}\" ({columnsStr}) VALUES {string.Join(", ", rowsQuery)};";
                        using var insertCmd = new NpgsqlCommand(query, destConn);
                        insertCmd.Parameters.AddRange(commandParameters.ToArray());
                        await insertCmd.ExecuteNonQueryAsync();
                    }
                    Console.WriteLine($"Đã ghi xong {dt.Rows.Count} bản ghi vào bảng \"{table}\" trên VPS.");
                }
            }

            using (var cmd = new NpgsqlCommand("SET session_replication_role = 'origin';", destConn))
            {
                await cmd.ExecuteNonQueryAsync();
            }
            Console.WriteLine("Đã bật lại các ràng buộc khóa ngoại (origin mode) trên VPS.");

            foreach (var table in tables)
            {
                bool hasId = false;
                using (var cmd = new NpgsqlCommand($@"
                    SELECT EXISTS (
                        SELECT 1 
                        FROM information_schema.columns 
                        WHERE table_name='{table}' AND column_name='Id'
                    );", destConn))
                {
                    hasId = (bool)(await cmd.ExecuteScalarAsync() ?? false);
                }

                if (hasId)
                {
                    using var seqCmd = new NpgsqlCommand($@"
                        SELECT pg_get_serial_sequence('""{table}""', 'Id');", destConn);
                    var seqNameObj = await seqCmd.ExecuteScalarAsync();
                    if (seqNameObj != null && seqNameObj != DBNull.Value && !string.IsNullOrEmpty(seqNameObj.ToString()))
                    {
                        string seqName = seqNameObj.ToString()!;
                        Console.WriteLine($"Đang đồng bộ sequence {seqName} cho bảng \"{table}\"...");
                        using var resetCmd = new NpgsqlCommand($@"
                            SELECT setval('{seqName}', COALESCE(MAX(""Id""), 1)) FROM ""{table}"";", destConn);
                        await resetCmd.ExecuteNonQueryAsync();
                    }
                }
            }

            Console.WriteLine("=== HOÀN THÀNH RESTORE DỮ LIỆU THÀNH CÔNG ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LỖI MẤT KẾT NỐI HOẶC DI TRÚ: {ex.Message}");
            Console.WriteLine(ex.ToString());
        }

        Environment.Exit(0);
    }
}
