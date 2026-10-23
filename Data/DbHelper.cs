using AutoPartsApp.Models;
using Npgsql;

namespace AutoPartsApp.Data;

public class DbHelper
{
    private readonly string _connectionString;

    public DbHelper(string connectionString)
    {
        _connectionString = connectionString;
    }

    // Инициализация таблицы и добавление начальных данных при старте
    public void Initialize()
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();

        var createTableSql = @"
            CREATE TABLE IF NOT EXISTS parts (
                id SERIAL PRIMARY KEY,
                name VARCHAR(200) NOT NULL,
                article VARCHAR(50) NOT NULL,
                manufacturer VARCHAR(100) NOT NULL,
                price NUMERIC(18, 2) NOT NULL,
                stock INTEGER NOT NULL DEFAULT 0,
                description VARCHAR(500)
            );";

        using (var command = new NpgsqlCommand(createTableSql, connection))
        {
            command.ExecuteNonQuery();
        }

        var countSql = "SELECT COUNT(*) FROM parts;";
        long count = 0;
        using (var command = new NpgsqlCommand(countSql, connection))
        {
            count = (long)(command.ExecuteScalar() ?? 0);
        }

        if (count == 0)
        {
            var seedSql = @"
                INSERT INTO parts (name, article, manufacturer, price, stock, description) VALUES 
                ('Тормозная колодка', 'BC-1234', 'Bosch', 2500.00, 40, 'Передняя, для легковых авто'),
                ('Масляный фильтр', 'OF-5678', 'Mann', 850.00, 120, 'Универсальный'),
                ('Свеча зажигания', 'SP-9012', 'NGK', 450.00, 200, 'Иридиевая');";

            using (var command = new NpgsqlCommand(seedSql, connection))
            {
                command.ExecuteNonQuery();
            }
        }
    }

    // Получение списка всех запчастей
    public List<Part> GetAllParts()
    {
        var parts = new List<Part>();
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();

        var sql = "SELECT id, name, article, manufacturer, price, stock, description FROM parts ORDER BY id;";
        using var command = new NpgsqlCommand(sql, connection);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            parts.Add(new Part
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Article = reader.GetString(2),
                Manufacturer = reader.GetString(3),
                Price = reader.GetDecimal(4),
                Stock = reader.GetInt32(5),
                Description = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }
        return parts;
    }

    // Получение одной запчасти по её ID
    public Part? GetPartById(int id)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();

        var sql = "SELECT id, name, article, manufacturer, price, stock, description FROM parts WHERE id = @id;";
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@id", id);

        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return new Part
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Article = reader.GetString(2),
                Manufacturer = reader.GetString(3),
                Price = reader.GetDecimal(4),
                Stock = reader.GetInt32(5),
                Description = reader.IsDBNull(6) ? null : reader.GetString(6)
            };
        }
        return null;
    }

    // Добавление новой запчасти в базу
    public void AddPart(Part part)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();

        var sql = @"
            INSERT INTO parts (name, article, manufacturer, price, stock, description) 
            VALUES (@name, @article, @manufacturer, @price, @stock, @description);";

        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@name", part.Name);
        command.Parameters.AddWithValue("@article", part.Article);
        command.Parameters.AddWithValue("@manufacturer", part.Manufacturer);
        command.Parameters.AddWithValue("@price", part.Price);
        command.Parameters.AddWithValue("@stock", part.Stock);
        command.Parameters.AddWithValue("@description", (object?)part.Description ?? DBNull.Value);

        command.ExecuteNonQuery();
    }

    // Атомарное уменьшение остатка на складе при покупке
    public bool DecreaseStock(int partId, int quantity)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();

        var sql = "UPDATE parts SET stock = stock - @qty WHERE id = @id AND stock >= @qty;";
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("@qty", quantity);
        command.Parameters.AddWithValue("@id", partId);

        int rowsAffected = command.ExecuteNonQuery();
        return rowsAffected > 0;
    }
}
