using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

string connectionString = "Host=localhost;Port=5432;Database=npgsql_tasks;Username=postgres;Password=1234;Maximum Pool Size=5;";

Console.WriteLine("=== Запуск Задачи 9: SemaphoreSlim ===");

var processor = new ParallelProcessor(connectionString);
await processor.RunParallelAsync(queryCount: 20, maxConcurrency: 4, CancellationToken.None);

Console.WriteLine("\nНажмите любую клавишу для выхода...");

public class ParallelProcessor
{
    private readonly string _connectionString;

    public ParallelProcessor(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task RunParallelAsync(int queryCount, int maxConcurrency, CancellationToken token)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new List<Task>();

        for (int i = 1; i <= queryCount; i++)
        {
            int taskId = i;
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    await ExecuteQueryAsync(taskId, token).ConfigureAwait(false);
                }
                finally
                {
                    semaphore.Release();
                }
            }, token));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        Console.WriteLine("\n[ИТОГ]: Все параллельные задачи успешно обработаны.");
    }

    private async Task ExecuteQueryAsync(int id, CancellationToken token)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);

            await using var command = new NpgsqlCommand("SELECT pg_sleep(0.5), @id", connection);
            command.Parameters.AddWithValue("id", id);

            Console.WriteLine($"[Старт]: Задача #{id} заняла соединение из пула.");
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            Console.WriteLine($"[Успех]: Задача #{id} завершена и вернула слот в пул.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Ошибка] Задача #{id}: {ex.Message}");
        }
    }
}
