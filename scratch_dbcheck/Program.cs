using System;
using Npgsql;

class Program
{
    static void Main()
    {
        string connString = "Host=163.61.73.83;Port=5432;Database=ClothingRental;Username=postgres;Password=123123@";
        
        try
        {
            using var conn = new NpgsqlConnection(connString);
            conn.Open();
            Console.WriteLine("Successfully connected to the database.");

            // Select last 3 orders and their transactions
            string query = @"
                SELECT o.""Id"" AS ""OrderId"", o.""Code"", o.""TotalPrice"", o.""DiscountAmount"", o.""FinalAmount"",
                       t.""Type"", t.""Amount"", t.""TransactionDate""
                FROM ""Orders"" o
                JOIN ""Transactions"" t ON o.""Id"" = t.""OrderId""
                ORDER BY o.""Id"" DESC
                LIMIT 30";
            
            using (var cmd = new NpgsqlCommand(query, conn))
            using (var reader = cmd.ExecuteReader())
            {
                int currentOrderId = -1;
                while (reader.Read())
                {
                    int orderId = Convert.ToInt32(reader["OrderId"]);
                    if (orderId != currentOrderId)
                    {
                        Console.WriteLine($"\nOrder Code: {reader["Code"]}, TotalPrice: {reader["TotalPrice"]}, DiscountAmount: {reader["DiscountAmount"]}, FinalAmount: {reader["FinalAmount"]}");
                        currentOrderId = orderId;
                    }
                    Console.WriteLine($"  - Transaction: Type: {reader["Type"]}, Amount: {reader["Amount"]}, Date: {reader["TransactionDate"]}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("An error occurred: " + ex.Message);
        }
    }
}
