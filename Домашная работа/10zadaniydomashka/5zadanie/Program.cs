using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;


string connectionString = "Host=localhost;Port=5432;Database=npgsql_tasks;Username=postgres;Password=1234;";

Console.WriteLine("=== Запуск Задачи 5: Оптимизация через NpgsqlBatch ===");
Console.WriteLine("Перед запуском убедись, что в таблице orders есть данные!\n");

var reportBuilder = new ReportBuilder(connectionString);

try
{
    Console.WriteLine("Запрашиваем отчет по клиенту ID = 1 (список заказов + общая сумма)...");
    Console.WriteLine("[Сеть]: Отправляем ОДИН пакет с двумя запросами на сервер...");

    var report = await reportBuilder.BuildReportAsync(customerId: 1, CancellationToken.None);

    Console.WriteLine("\n[РЕЗУЛЬТАТЫ ОТЧЕТА]:");
    Console.WriteLine($"Всего найдено заказов: {report.Orders.Count}");
    foreach (var orderInfo in report.Orders)
    {
        Console.WriteLine($" -> {orderInfo}");
    }

    Console.WriteLine($"\nИтоговая сумма всех заказов клиента: {report.TotalAmount} руб.");
}
catch (Exception ex)
{
    Console.WriteLine($"\n[ОШИБКА]: {ex.Message}");
}

Console.WriteLine("\nНажмите любую клавишу для выхода...");


public class ReportData
{
    public List<string> Orders { get; set; } = new();
    public decimal TotalAmount { get; set; }
}

public class ReportBuilder
{
    private readonly string _connectionString;

    public ReportBuilder(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<ReportData> BuildReportAsync(int customerId, CancellationToken token)
    {
        var report = new ReportData();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);

        await using var batch = new NpgsqlBatch(connection)
        {
            BatchCommands =
            {
                new NpgsqlBatchCommand("SELECT id, amount FROM orders WHERE customer_id = @cId")
                {
                    Parameters = { new NpgsqlParameter("cId", customerId) }
                },
                new NpgsqlBatchCommand("SELECT COALESCE(SUM(amount), 0) FROM orders WHERE customer_id = @cId")
                {
                    Parameters = { new NpgsqlParameter("cId", customerId) }
                }
            }
        };

        await using var reader = await batch.ExecuteReaderAsync(token).ConfigureAwait(false);

        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            int orderId = reader.GetInt32(0);
            decimal amount = reader.GetDecimal(1);
            report.Orders.Add($"Заказ #{orderId} на сумму {amount} руб.");
        }

        if (await reader.NextResultAsync(token).ConfigureAwait(false))
        {
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                report.TotalAmount = reader.GetDecimal(0);
            }
        }

        return report;
    }
}
