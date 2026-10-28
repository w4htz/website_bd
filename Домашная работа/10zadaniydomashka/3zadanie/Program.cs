using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

string connectionString = "Host=localhost;Port=5432;Database=npgsql_tasks;Username=postgres;Password=1234;";

Console.WriteLine("=== Запуск Задачи 3: Асинхронные транзакции (Перевод денег) ===");
Console.WriteLine("Перед запуском убедись, что выполнил SQL-скрипт для таблицы accounts в pgAdmin!\n");

var accountService = new AccountService(connectionString);

try
{
    Console.WriteLine("Пытаемся перевести 1500 рублей со счета №1 на счет №2...");

    await accountService.TransferAsync(fromId: 1, toId: 2, amount: 1500.00m, CancellationToken.None);

    Console.WriteLine("\n[УСПЕХ]: Транзакция успешно зафиксирована (Commit)!");
}
catch (Exception ex)
{
    Console.WriteLine($"\n[ОШИБКА И ОТКАТ]: Операция прервана. {ex.Message}");
}

Console.WriteLine("\nНажмите любую клавишу для выхода...");


public class AccountService
{
    private readonly string _connectionString;

    public AccountService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task TransferAsync(int fromId, int toId, decimal amount, CancellationToken userToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(userToken).ConfigureAwait(false);


        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, userToken).ConfigureAwait(false);

        try
        {
            const string checkSql = "SELECT balance FROM accounts WHERE id = @id FOR UPDATE";
            await using var cmdCheck = new NpgsqlCommand(checkSql, connection, transaction);
            cmdCheck.Parameters.AddWithValue("id", fromId);

            var currentBalance = (decimal)(await cmdCheck.ExecuteScalarAsync(userToken).ConfigureAwait(false) ?? 0m);

            if (currentBalance < amount)
            {
                throw new InvalidOperationException($"Недостаточно средств на счете {fromId}. Текущий баланс: {currentBalance} руб.");
            }

            const string withdrawSql = "UPDATE accounts SET balance = balance - @amount WHERE id = @id";
            await using var cmdWithdraw = new NpgsqlCommand(withdrawSql, connection, transaction);
            cmdWithdraw.Parameters.AddWithValue("amount", amount);
            cmdWithdraw.Parameters.AddWithValue("id", fromId);
            await cmdWithdraw.ExecuteNonQueryAsync(userToken).ConfigureAwait(false);

            const string depositSql = "UPDATE accounts SET balance = balance + @amount WHERE id = @id";
            await using var cmdDeposit = new NpgsqlCommand(depositSql, connection, transaction);
            cmdDeposit.Parameters.AddWithValue("amount", amount);
            cmdDeposit.Parameters.AddWithValue("id", toId);
            await cmdDeposit.ExecuteNonQueryAsync(userToken).ConfigureAwait(false);

            await transaction.CommitAsync(userToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Система]: Произошел сбой. Инициируем RollbackAsync...");

            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await transaction.RollbackAsync(cleanupCts.Token).ConfigureAwait(false);
            }
            catch (Exception rollbackEx)
            {
                throw new AggregateException("Критическая ошибка при попытке откатить транзакцию.", ex, rollbackEx);
            }
            throw;
        }
    }
}
