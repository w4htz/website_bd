using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

string connectionString = "Host=localhost;Port=5432;Database=npgsql_tasks;Username=postgres;Password=1234;";

Console.WriteLine("=== Запуск Задачи 8: Пакетная вставка с EnableErrorBarriers ===");
Console.WriteLine("Перед запуском убедись, что таблица batch_items создана в pgAdmin!\n");

var service = new BatchErrorBarrierService(connectionString);

await service.InsertWithBarriersAsync();

await service.InsertWithTransactionAndBarriersAsync();

Console.WriteLine("\nНажмите любую клавишу для выхода...");


public class BatchErrorBarrierService
{
    private readonly string _connectionString;

    public BatchErrorBarrierService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InsertWithBarriersAsync()
    {
        Console.WriteLine("\n--- Тест 1: Запуск NpgsqlBatch БЕЗ транзакции (EnableErrorBarriers = true) ---");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);


        await using (var cmdClear = new NpgsqlCommand("TRUNCATE TABLE batch_items", connection))
        {
            await cmdClear.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using var batch = new NpgsqlBatch(connection)
        {

            EnableErrorBarriers = true,
            BatchCommands =
            {
                new NpgsqlBatchCommand("INSERT INTO batch_items (id, name) VALUES (1, 'Item 1')"),
                new NpgsqlBatchCommand("INSERT INTO batch_items (id, name) VALUES (1, 'Duplicate ID')"),
                new NpgsqlBatchCommand("INSERT INTO batch_items (id, name) VALUES (3, 'Item 3')")
            }
        };

        int successfulRows = 0;
        int errorRows = 0;

        await using var reader = await batch.ExecuteReaderAsync().ConfigureAwait(false);
        do
        {
            try
            {

                successfulRows += reader.RecordsAffected > 0 ? reader.RecordsAffected : 0;
            }
            catch (PostgresException ex)
            {
                errorRows++;
                Console.WriteLine($"[Частичная ошибка]: Одна из команд упала. Код: {ex.SqlState}. Текст: {ex.MessageText}");
            }
        } while (await reader.NextResultAsync().ConfigureAwait(false));

        await using var cmdCheck = new NpgsqlCommand("SELECT COUNT(*) FROM batch_items", connection);
        long count = (long)(await cmdCheck.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);

        Console.WriteLine($"[ИТОГ Теста 1]: Обработано ошибок: {errorRows}. Реально строк в базе: {count} (Должно быть 2: Item 1 и Item 3)");
    }

    public async Task InsertWithTransactionAndBarriersAsync()
    {
        Console.WriteLine("\n--- Тест 2: Запуск NpgsqlBatch ВНУТРИ явной транзакции ---");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using (var cmdClear = new NpgsqlCommand("TRUNCATE TABLE batch_items", connection))
        {
            await cmdClear.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using var tx = await connection.BeginTransactionAsync().ConfigureAwait(false);

        await using var batch = new NpgsqlBatch(connection, tx)
        {
            EnableErrorBarriers = true,
            BatchCommands =
            {
                new NpgsqlBatchCommand("INSERT INTO batch_items (id, name) VALUES (10, 'Item 10')"),
                new NpgsqlBatchCommand("INSERT INTO batch_items (id, name) VALUES (10, 'Duplicate ID')"),
                new NpgsqlBatchCommand("INSERT INTO batch_items (id, name) VALUES (30, 'Item 30')")
            }
        };

        try
        {
            await using var reader = await batch.ExecuteReaderAsync().ConfigureAwait(false);
            do { /* Пробегаем по результатам */ } while (await reader.NextResultAsync().ConfigureAwait(false));

            await tx.CommitAsync().ConfigureAwait(false);
        }
        catch (PostgresException ex)
        {
            Console.WriteLine($"[Ошибка в транзакции]: Пакет прерван. Код СУБД: {ex.SqlState}. Описание: {ex.MessageText}");

            await tx.RollbackAsync().ConfigureAwait(false);
        }

        await using var cmdCheck = new NpgsqlCommand("SELECT COUNT(*) FROM batch_items", connection);
        long count = (long)(await cmdCheck.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
        Console.WriteLine($"[ИТОГ Теста 2]: Транзакция упала. Реально строк в базе: {count} (Должно быть 0, так как вся транзакция откатилась)");
    }
}
