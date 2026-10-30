using System.Data;
using System.Diagnostics;
using Dapper;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDeveloperExceptionPage();

// Строка подключения к вашей локальной БД PostgreSQL с паролем 1234
string cs = "Host=localhost;Port=5432;Database=shopdb;Username=postgres;Password=1234;Pooling=true;Minimum Pool Size=1;Maximum Pool Size=20";

// 1. Тест пула соединений
app.MapGet("/pool-compare", () =>
{
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < 5; i++)
    {
        using (var conn = new NpgsqlConnection(cs))
        {
            conn.Open();
        }
    }
    sw.Stop();
    long timeWithPooling = sw.ElapsedMilliseconds;

    var noPoolingCs = cs.Replace("Pooling=true", "Pooling=false");
    sw.Restart();
    for (int i = 0; i < 5; i++)
    {
        using (var conn = new NpgsqlConnection(noPoolingCs))
        {
            conn.Open();
        }
    }
    sw.Stop();
    long timeWithoutPooling = sw.ElapsedMilliseconds;

    return Results.Text($"С пулом (Pooling=true): {timeWithPooling} ms\nБез пула (Pooling=false): {timeWithoutPooling} ms");
});

// 2. Закрытие доставки в транзакции
app.MapPost("/shipments/close", (int id) =>
{
    using (var conn = new NpgsqlConnection(cs))
    {
        conn.Open();
        using (var tx = conn.BeginTransaction())
        {
            try
            {
                using (var cmd = new NpgsqlCommand("UPDATE shipments SET status = 'closed' WHERE id = @id", conn, tx))
                {
                    cmd.Parameters.AddWithValue("id", id);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
                return Results.Ok(new { message = $"Shipment {id} closed successfully." });
            }
            catch
            {
                tx.Rollback();
                return Results.Problem("Transaction failed and rolled back.");
            }
        }
    }
});

// 3. Выборка через Dapper
app.MapGet("/shipments/dapper", () =>
{
    using (var conn = new NpgsqlConnection(cs))
    {
        conn.Open();
        var result = conn.Query("SELECT * FROM shipments").ToList();
        return Results.Json(result);
    }
});

// 4. Отчет через DataSet / DataAdapter / DataTable
app.MapGet("/shipments/table", () =>
{
    using (var conn = new NpgsqlConnection(cs))
    {
        conn.Open();
        using (var da = new NpgsqlDataAdapter("SELECT * FROM shipments", conn))
        {
            var ds = new DataSet();
            da.Fill(ds, "ShipmentsReport");
            
            var dt = ds.Tables["ShipmentsReport"];
            var list = new List<object>();
            
            if (dt != null)
            {
                foreach (DataRow row in dt.Rows)
                {
                    list.Add(new { id = row["id"], code = row["code"], status = row["status"] });
                }
            }
            return Results.Json(list);
        }
    }
});

app.Run();
