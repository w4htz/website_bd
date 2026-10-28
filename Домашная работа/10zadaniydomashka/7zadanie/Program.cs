using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

string connectionString = "Host=localhost;Port=5432;Database=npgsql_tasks;Username=postgres;Password=1234;Timeout=5";

Console.WriteLine("=== Запуск Задачи 7: Health Check с таймаутами ===");

var hc = new DatabaseHealthCheck(connectionString);

Console.WriteLine("--- Тест 1: Стандартная проверка работоспособности ---");
await hc.CheckHealthAsync(simulateHang: false);

Console.WriteLine("\n--- Тест 2: Имитация зависшего запроса (pg_sleep) ---");
await hc.CheckHealthAsync(simulateHang: true);

Console.WriteLine("\nНажмите любую клавишу для выхода...");


public class DatabaseHealthCheck
{
    private readonly string _connectionString;

    public DatabaseHealthCheck(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task CheckHealthAsync(bool simulateHang)
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(3));

        var sw = Stopwatch.StartNew();
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cts.Token).ConfigureAwait(false);

            await using (var cmdTimeout = new NpgsqlCommand("SET statement_timeout = 2000", connection))
            {
                await cmdTimeout.ExecuteNonQueryAsync(cts.Token).ConfigureAwait(false);
            }

            string sql = simulateHang ? "SELECT pg_sleep(10)" : "SELECT 1";

            await using var command = new NpgsqlCommand(sql, connection);
            var result = await command.ExecuteScalarAsync(cts.Token).ConfigureAwait(false);

            sw.Stop();
            Console.WriteLine($"[СТАТУС]: Healthy. База ответила за {sw.ElapsedMilliseconds} мс. Ответ: {result}");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            Console.WriteLine($"[СТАТУС]: Unhealthy! Превышен лимит CancellationToken (3 сек). Время: {sw.ElapsedMilliseconds} мс");
        }
        catch (PostgresException ex) when (ex.SqlState == "57014")
        {
            sw.Stop();
            Console.WriteLine($"[СТАТУС]: Degraded. Сработал серверный statement_timeout (2 сек).");
            Console.WriteLine($"Время до отсечки: {sw.ElapsedMilliseconds} мс. Сообщение: {ex.MessageText}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            Console.WriteLine($"[СТАТУС]: Unhealthy! Ошибка: {ex.Message}. Время: {sw.ElapsedMilliseconds} мс");
        }
    }
}
