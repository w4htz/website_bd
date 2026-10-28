using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Npgsql;

// ============================================================================
// НАСТРОЙКА ПОДКЛЮЧЕНИЯ (Пароль 1234 установлен)
// В строке подключения жестко прописано:
// Maximum Pool Size=5 — в пуле может быть не больше 5 открытых соединений
// Timeout=5 — если свободных мест в пуле нет, ждать не дольше 5 секунд
// ============================================================================
string connectionString = "Host=localhost;Port=5432;Database=npgsql_tasks;Username=postgres;Password=1234;Maximum Pool Size=5;Timeout=5";

Console.WriteLine("=== Запуск Задачи 6: Исчерпание и диагностика пула соединений ===");

var diagnostic = new ConnectionPoolDiagnostic(connectionString);

try
{
    await diagnostic.ExhaustPoolAsync(count: 6);
}
catch (Exception ex)
{
    Console.WriteLine($"\n[ОЖИДАЕМАЯ ОШИБКА ПУЛА]: {ex.Message}");
}

await diagnostic.DiagnosePoolAsync();

diagnostic.HealPool();

try
{
    Console.WriteLine("\nПроверяем работу пула после очистки...");
    await using var cleanConn = new NpgsqlConnection(connectionString);
    await cleanConn.OpenAsync();
    Console.WriteLine("[УСПЕХ]: Новое соединение открыто без проблем!");
}
catch (Exception ex)
{
    Console.WriteLine($"[ОШИБКА]: {ex.Message}");
}

Console.WriteLine("\nНажмите любую клавишу для выхода...");



public class ConnectionPoolDiagnostic
{
    private readonly string _connectionString;
    private readonly List<NpgsqlConnection> _leakedConnections = new();

    public ConnectionPoolDiagnostic(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task ExhaustPoolAsync(int count)
    {
        Console.WriteLine($"\nПытаемся открыть {count} соединений подряд (Лимит пула = 5)...");

        for (int i = 1; i <= count; i++)
        {
            var connection = new NpgsqlConnection(_connectionString);
            var sw = Stopwatch.StartNew();

            await connection.OpenAsync().ConfigureAwait(false);
            sw.Stop();

            _leakedConnections.Add(connection);
            Console.WriteLine($"Соединение #{i} успешно занято за {sw.ElapsedMilliseconds} мс.");
        }
    }

    public async Task DiagnosePoolAsync()
    {
        Console.WriteLine("\n--- Диагностика пула ---");
        var sw = Stopwatch.StartNew();
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);

            await conn.OpenAsync();
        }
        catch (NpgsqlException ex) when (ex.Message.Contains("pool has been exhausted") || ex.InnerException is TimeoutException)
        {
            sw.Stop();
            Console.WriteLine($"[ДИАГНОСТИКА УСПЕШНА]: Зафиксировано исчерпание пула клиента.");
            Console.WriteLine($"Время ожидания до падения: {sw.ElapsedMilliseconds} мс (соответствует Timeout=5).");
            Console.WriteLine($"Текст ошибки драйвера: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Неожиданная ошибка: {ex.Message}");
        }
    }
    public void HealPool()
    {
        Console.WriteLine("\n[ЛЕКАРЬ]: Принудительно закрываем утекшие сокеты...");
        foreach (var conn in _leakedConnections)
        {
            conn.Dispose();
        }
        _leakedConnections.Clear();

        NpgsqlConnection.ClearAllPools();
        Console.WriteLine("[СИСТЕМА]: Клиентский пул очищен и сброшен.");
    }
}
