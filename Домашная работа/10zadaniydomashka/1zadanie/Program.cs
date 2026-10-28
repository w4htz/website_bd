using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

string connectionString = "Host=localhost;Port=5432;Database=npgsql_tasks;Username=postgres;Password=1234;";

Console.WriteLine("=== Запуск: Асинхронный репозиторий сотрудников ===");
Console.WriteLine("Перед запуском убедись, что выполнил SQL-скрипт в pgAdmin!\n");

var repo = new EmployeeRepository(connectionString);

try
{
    Console.WriteLine("Запрашиваем сотрудников (Страница 1, размер страницы 5)...");

    var employees = await repo.GetEmployeesAsync(page: 1, pageSize: 5, CancellationToken.None);

    Console.WriteLine("\n[РЕЗУЛЬТАТ ЗАПРОСА]:");
    foreach (var emp in employees)
    {
        Console.WriteLine($"ID: {emp.Id} | {emp.FirstName} {emp.LastName} | ЗП: {emp.Salary} руб. | Email: {emp.Email}");
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("\n[ОШИБКА]: Операция была прервана из-за таймаута на стороне C# клиента.");
}
catch (PostgresException ex) when (ex.SqlState == "57014")
{
    Console.WriteLine($"\n[ОШИБКА СЕРВЕРА]: Запрос отменен самой базой данных (SqlState 57014). Детали: {ex.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"\n[ОШИБКА]: {ex.Message}");
}

Console.WriteLine("\nНажмите любую клавишу для выхода...");
public class Employee
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal Salary { get; set; }
}

public class EmployeeRepository
{
    private readonly string _connectionString;

    public EmployeeRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<Employee>> GetEmployeesAsync(int page, int pageSize, CancellationToken userToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(userToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var employees = new List<Employee>();
        int offset = (page - 1) * pageSize;

        // Открываем соединение
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cts.Token).ConfigureAwait(false);

        const string sql = "SELECT id, first_name, last_name, email, salary FROM employees ORDER BY id LIMIT @limit OFFSET @offset";

        // Создаем команду
        await using var command = new NpgsqlCommand(sql, connection);
        command.CommandTimeout = 10;

        command.Parameters.AddWithValue("limit", pageSize);
        command.Parameters.AddWithValue("offset", offset);

        // Получаем reader
        await using var reader = await command.ExecuteReaderAsync(cts.Token).ConfigureAwait(false);

        while (await reader.ReadAsync(cts.Token).ConfigureAwait(false))
        {
            employees.Add(new Employee
            {
                Id = reader.GetInt32(0),
                FirstName = reader.GetString(1),
                LastName = reader.GetString(2),
                Email = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                Salary = reader.GetDecimal(4)
            });
        }

        return employees;
    }
}
