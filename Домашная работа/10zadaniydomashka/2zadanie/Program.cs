using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;

string connectionString = "Host=localhost;Port=5432;Database=npgsql_tasks;Username=postgres;Password=1234;";

Console.WriteLine("=== Запуск Задачи 2: Массовый импорт через COPY ===");
Console.WriteLine("Перед запуском убедись, что выполнил SQL-скрипт для таблицы orders в pgAdmin!\n");

var importer = new OrderBulkImporter(connectionString);

try
{
Console.WriteLine("Генерируем 50 000 тестовых заказов в памяти...");
var testOrders = new List<Order>();
for (int i = 1; i <= 50000; i++)
{
testOrders.Add(new Order
{
Id = i,
CustomerId = (i % 10) + 1,
Amount = (decimal)(i * 0.5),
CreatedAt = DateTime.UtcNow
});
}

Console.WriteLine("Начинаем высокоскоростной бинарный импорт...");

// Создаем объект прогресса, который будет выводить статус в консоль
var progress = new Progress<int>(rows =>
{
Console.WriteLine($"[ПРОГРЕСС]: Успешно записано строк в базу: {rows}");
});

await importer.BulkCopyAsync(testOrders, progress, CancellationToken.None);
Console.WriteLine("\n[УСПЕХ]: Все данные мгновенно скопированы!");
}
catch (Exception ex)
{
Console.WriteLine($"\n[ОШИБКА]: {ex.Message}");
}

Console.WriteLine("\nНажмите любую клавишу для выхода...");

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class OrderBulkImporter
{
    private readonly string _connectionString;

    public OrderBulkImporter(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task BulkCopyAsync(IEnumerable<Order> orders, IProgress<int> progress, CancellationToken token)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);

        try
        {
            // Открываем бинарный поток COPY для таблицы orders. 
            // Это самый быстрый способ записи в PostgreSQL.
            await using var importer = await connection.BeginBinaryImportAsync(
                "COPY orders (id, customer_id, amount, created_at) FROM STDIN (FORMAT BINARY)", token)
                .ConfigureAwait(false);

            int rowsWritten = 0;

            foreach (var order in orders)
            {
                token.ThrowIfCancellationRequested();

                // Начинаем запись новой строки
                await importer.StartRowAsync(token).ConfigureAwait(false);

                // Записываем каждое поле с явным указанием типов данных NpgsqlDbType
                await importer.WriteAsync(order.Id, NpgsqlDbType.Integer, token).ConfigureAwait(false);
                await importer.WriteAsync(order.CustomerId, NpgsqlDbType.Integer, token).ConfigureAwait(false);
                await importer.WriteAsync(order.Amount, NpgsqlDbType.Numeric, token).ConfigureAwait(false);
                await importer.WriteAsync(order.CreatedAt, NpgsqlDbType.TimestampTz, token).ConfigureAwait(false);

                rowsWritten++;

                // Каждые 10 000 строк уведомляем вызывающий поток о прогрессе
                if (rowsWritten % 10000 == 0)
                {
                    progress?.Report(rowsWritten);
                }
            }

            await importer.CompleteAsync(token).ConfigureAwait(false);
        }
        catch
        {

            throw;
        }
    }
}
