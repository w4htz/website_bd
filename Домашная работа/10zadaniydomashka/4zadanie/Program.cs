using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

string connectionString = "Host=localhost;Port=5432;Database=npgsql_tasks;Username=postgres;Password=1234;";

Console.WriteLine("=== Запуск Задачи 4: Потоковый экспорт BLOB (SequentialAccess) ===");
Console.WriteLine("Перед запуском убедись, что выполнил SQL-скрипт в pgAdmin!\n");

var exporter = new DocumentExporter(connectionString);

string outputDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExportedDocs");

try
{
    Console.WriteLine($"Начинаем экспорт файлов в директорию:\n--> {outputDirectory}\n");

    await exporter.ExportDocumentsAsync(outputDirectory, CancellationToken.None);

    Console.WriteLine("\n[УСПЕХ]: Все документы успешно выгружены без перегрузки памяти!");
}
catch (Exception ex)
{
    Console.WriteLine($"\n[ОШИБКА]: {ex.Message}");
}

Console.WriteLine("\nНажмите любую клавишу для выхода...");

public class DocumentExporter
{
    private readonly string _connectionString;

    public DocumentExporter(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task ExportDocumentsAsync(string outputDir, CancellationToken token)
    {
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);

        const string sql = "SELECT id, file_name, content FROM documents ORDER BY id";

        await using var command = new NpgsqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false);

        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {

            int id = reader.GetInt32(0);
            string fileName = reader.GetString(1);

            string fullPath = Path.Combine(outputDir, fileName);
            Console.WriteLine($"[Файл найден]: ID {id} | Имя: {fileName} -> Скачиваем поток...");

            byte[] fileBytes = await reader.GetFieldValueAsync<byte[]>(2, token).ConfigureAwait(false);

            await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await fileStream.WriteAsync(fileBytes, token).ConfigureAwait(false);

            Console.WriteLine($"[Cохранено]: Файл успешно записан на диск.");
        }
    }
}
