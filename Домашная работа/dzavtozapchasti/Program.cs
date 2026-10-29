using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace PartsApp
{
    // ==========================================
    // ТОЧКА ВХОДА (ИНИЦИАЛИЗАЦИЯ ВЕБ-СЕРВЕРА)
    // ==========================================
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.Services.AddControllersWithViews();

            // Строка подключения с твоим паролем 1234
            var connectionString = "Host=localhost;Database=parts_db;Username=postgres;Password=1234;";

            var dbHelper = new DbHelper(connectionString);
            dbHelper.Initialize(); // Создание таблиц и ограничений UNIQUE

            builder.Services.AddSingleton(dbHelper);
            var app = builder.Build();

            app.UseStaticFiles();
            app.UseRouting();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Parts}/{action=Search}/{id?}");

            app.Run();
        }
    }

    // ==========================================
    // МОДЕЛЬ ДАННЫХ
    // ==========================================
    public class Part
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Article { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
    }
    // ==========================================
    // 🛠️ СЛОЙ ДАННЫХ: DbHelper (Начало)
    // ==========================================
    public class DbHelper
    {
        private readonly string _connectionString;

        public DbHelper(string connectionString)
        {
            _connectionString = connectionString;
        }

        // Задача 9: Инициализация структуры базы
        public void Initialize()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            string initSql = @"
                CREATE TABLE IF NOT EXISTS parts (
                    id SERIAL PRIMARY KEY, name VARCHAR(255) NOT NULL, article VARCHAR(50) NOT NULL, price NUMERIC(12,2) NOT NULL, stock INT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS orders (
                    id SERIAL PRIMARY KEY, part_id INT NOT NULL, quantity INT NOT NULL, total NUMERIC(12,2) NOT NULL, created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                );";
            using var cmd = new NpgsqlCommand(initSql, conn);
            cmd.ExecuteNonQuery();

            string constraintSql = @"
                DO $$ BEGIN 
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_parts_article') THEN
                        ALTER TABLE parts ADD CONSTRAINT uq_parts_article UNIQUE (article);
                    END IF;
                END $$;";
            using var cmdConstraint = new NpgsqlCommand(constraintSql, conn);
            cmdConstraint.ExecuteNonQuery();
        }

        // 1. Поиск запчастей по названию и артикулу
        public List<Part> SearchParts(string query)
        {
            var parts = new List<Part>();
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();

            string sql = "SELECT id, name, article, price, stock FROM parts";
            if (!string.IsNullOrWhiteSpace(query)) sql += " WHERE name ILIKE @q OR article ILIKE @q";
            sql += " ORDER BY id";

            using var cmd = new NpgsqlCommand(sql, conn);
            if (!string.IsNullOrWhiteSpace(query)) cmd.Parameters.AddWithValue("@q", $"%{query}%");

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                parts.Add(new Part
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Article = reader.GetString(2),
                    Price = reader.GetDecimal(3),
                    Stock = reader.GetInt32(4)
                });
            }
            return parts;
        }

        // 2. Удаление запчасти с проверкой результата
        public bool DeletePart(int id)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand("DELETE FROM parts WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);
            return cmd.ExecuteNonQuery() > 0;
        }

        // 3. Статистика по складу одним запросом
        public (int totalParts, decimal totalValue, decimal avgPrice) GetStats()
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand("SELECT COUNT(*), COALESCE(SUM(price*stock),0), COALESCE(AVG(price),0) FROM parts", conn);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return (Convert.ToInt32(reader.GetValue(0)), reader.GetDecimal(1), reader.GetDecimal(2));
            }
            return (0, 0, 0);
        }

        // 4. Оформление заказа в транзакции
        public bool PlaceOrder(int partId, int quantity)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                decimal price = 0; int stock = 0;
                using (var c = new NpgsqlCommand("SELECT price, stock FROM parts WHERE id = @id FOR UPDATE", conn, tx))
                {
                    c.Parameters.AddWithValue("@id", partId);
                    using var r = c.ExecuteReader(); if (!r.Read()) { tx.Rollback(); return false; }
                    price = r.GetDecimal(0); stock = r.GetInt32(1);
                }
                if (stock < quantity) { tx.Rollback(); return false; }

                using (var c = new NpgsqlCommand("INSERT INTO orders (part_id, quantity, total) VALUES (@p, @q, @t)", conn, tx))
                {
                    c.Parameters.AddWithValue("@p", partId); c.Parameters.AddWithValue("@q", quantity); c.Parameters.AddWithValue("@t", price * quantity);
                    c.ExecuteNonQuery();
                }
                using (var c = new NpgsqlCommand("UPDATE parts SET stock = stock - @qty WHERE id = @id", conn, tx))
                {
                    c.Parameters.AddWithValue("@qty", quantity); c.Parameters.AddWithValue("@id", partId);
                    c.ExecuteNonQuery();
                }
                tx.Commit(); return true;
            }
            catch { tx.Rollback(); throw; }
        }

        // 5. Массовое поступление товара с откатом при ошибке
        public bool Restock(List<(int PartId, int AddQty)> items)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var tx = conn.BeginTransaction();
            try
            {
                foreach (var item in items)
                {
                    using var c = new NpgsqlCommand("UPDATE parts SET stock = stock + @qty WHERE id = @id", conn, tx);
                    c.Parameters.AddWithValue("@qty", item.AddQty); c.Parameters.AddWithValue("@id", item.PartId);
                    if (c.ExecuteNonQuery() == 0) { tx.Rollback(); return false; }
                }
                tx.Commit(); return true;
            }
            catch { tx.Rollback(); throw; }
        }
        // 6. «Сырое» чтение через NpgsqlDataReader
        public List<Dictionary<string, object?>> GetRawParts()
        {
            var result = new List<Dictionary<string, object?>>();
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand("SELECT * FROM parts ORDER BY id", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row.Add(reader.GetName(i), reader.IsDBNull(i) ? null : reader.GetValue(i));
                }
                result.Add(row);
            }
            return result;
        }

        // 7. DataTable через NpgsqlDataAdapter
        public DataTable GetPartsDataTable()
        {
            var dt = new DataTable();
            using var conn = new NpgsqlConnection(_connectionString);
            using var adapter = new NpgsqlDataAdapter("SELECT id, name, article, price, stock FROM parts ORDER BY id", conn);
            adapter.Fill(dt);
            return dt;
        }

        public DataSet GetPartsDataSet()
        {
            var ds = new DataSet();
            using var conn = new NpgsqlConnection(_connectionString);
            using var adapter = new NpgsqlDataAdapter("SELECT * FROM parts; SELECT * FROM orders;", conn);
            adapter.Fill(ds);
            return ds;
        }

        // 8. Асинхронная версия GetAllParts
        public async Task<List<Part>> GetAllPartsAsync()
        {
            var parts = new List<Part>();
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT id, name, article, price, stock FROM parts ORDER BY id", conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                parts.Add(new Part
                {
                    Id = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Article = reader.GetString(2),
                    Price = reader.GetDecimal(3),
                    Stock = reader.GetInt32(4)
                });
            }
            return parts;
        }

        // 9. Обработка уникального ограничения (SqlState)
        public (bool Ok, string? Error) TryAddPart(Part part)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand("INSERT INTO parts (name, article, price, stock) VALUES (@name, @article, @price, @stock)", conn);
            cmd.Parameters.AddWithValue("@name", part.Name); cmd.Parameters.AddWithValue("@article", part.Article);
            cmd.Parameters.AddWithValue("@price", part.Price); cmd.Parameters.AddWithValue("@stock", part.Stock);
            try
            {
                cmd.ExecuteNonQuery(); return (true, null);
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                return (false, "Запчасть с таким артикулом уже существует");
            }
        }
    }
    // ==========================================
    // 🎮 КОНТРОЛЛЕР И ПРЕДСТАВЛЕНИЯ (2 в 1)
    // ==========================================
    public class PartsController : Controller
    {
        private readonly DbHelper _db;
        private const string BootstrapCdn = "<link rel='stylesheet' href='https://jsdelivr.net'>";
        private readonly string _navMenu = $@"
            <nav class='navbar navbar-expand navbar-dark bg-dark mb-4'>
                <div class='container-fluid'>
                    <span class='navbar-brand'>⚙️ Автозапчасти ADO.NET</span>
                    <div class='navbar-nav'>
                        <a class='nav-link' href='/Parts/Search'>1. Поиск и 2. Удаление</a>
                        <a class='nav-link' href='/Parts/Stats'>3. Статистика</a>
                        <a class='nav-link' href='/Parts/Order'>4. Заказ</a>
                        <a class='nav-link' href='/Parts/Restock'>5. Поступление</a>
                        <a class='nav-link' href='/Parts/Raw'>6. Сырой Reader</a>
                        <a class='nav-link' href='/Parts/Tables'>7. DataTable</a>
                        <a class='nav-link' href='/Parts/Index'>8. Async GetAll</a>
                        <a class='nav-link' href='/Parts/Create'>9. Unique Check</a>
                        <a class='nav-link text-warning' href='/Parts/PoolTest' target='_blank'>10. Пул тест</a>
                    </div>
                </div>
            </nav>";

        public PartsController(DbHelper db) => _db = db;

        private ContentResult Html(string title, string body) =>
            Content($"<!DOCTYPE html><html><head><meta charset='utf-8'><title>{title}</title>{BootstrapCdn}</head><body class='bg-light'>{_navMenu}<div class='container'>{body}</div></body></html>", "text/html", Encoding.UTF8);

        // 1 & 2. Поиск и удаление товара
        public IActionResult Search(string query)
        {
            var parts = _db.SearchParts(query);
            var err = TempData["ErrorMessage"] != null ? $"<div class='alert alert-danger'>{TempData["ErrorMessage"]}</div>" : "";
            var ok = TempData["SuccessMessage"] != null ? $"<div class='alert alert-success'>{TempData["SuccessMessage"]}</div>" : "";
            var rows = new StringBuilder();
            foreach (var p in parts)
            {
                rows.Append($@"<tr><td>{p.Id}</td><td>{p.Name}</td><td>{p.Article}</td><td>{p.Price}</td><td>{p.Stock}</td>
                    <td><form method='post' action='/Parts/Delete' onsubmit=""return confirm('Реально удалить?')"" style='display:inline;'><input type='hidden' name='id' value='{p.Id}'/><button type='submit' class='btn btn-danger btn-sm'>Удалить</button></form></td></tr>");
            }
            return Html("Поиск и Удаление", $"{err}{ok}<h2>1. Поиск и 2. Удаление</h2><form method='get' action='/Parts/Search' class='mb-3'><div class='input-group'><input type='text' name='query' value='{query}' class='form-control' placeholder='Поиск...'/><button type='submit' class='btn btn-primary'>Искать</button></div></form><table class='table table-bordered bg-white'><thead><tr><th>ID</th><th>Название</th><th>Артикул</th><th>Цена</th><th>Склад</th><th>Действие</th></tr></thead><tbody>{rows}</tbody></table>");
        }

        [HttpPost] public IActionResult Delete(int id) { if (!_db.DeletePart(id)) TempData["ErrorMessage"] = "Не найден!"; else TempData["SuccessMessage"] = "Успешно удалено!"; return RedirectToAction(nameof(Search)); }

        // 3. Статистика по складу
        public IActionResult Stats() { var (t, v, a) = _db.GetStats(); return Html("Статистика", $"<h2>3. Статистика склада одним запросом</h2><div class='row mt-4'><div class='col-md-4'><div class='card p-3 bg-primary text-white'><h5>Позиций</h5><h3>{t}</h3></div></div><div class='col-md-4'><div class='card p-3 bg-success text-white'><h5>Стоимость</h5><h3>{v:C}</h3></div></div><div class='col-md-4'><div class='card p-3 bg-info text-white'><h5>Средняя цена</h5><h3>{a:C}</h3></div></div></div>"); }

        // 4. Оформление заказа в транзакции
        public IActionResult Order(string? msg, bool isErr = false) => Html("Заказ", $"{(msg != null ? $"<div class='alert alert-{(isErr ? "danger" : "success")}'>{msg}</div>" : "")}<h2>4. Оформление заказа (Транзакция)</h2><form method='post' action='/Parts/PlaceOrder' class='card p-4 bg-white mt-3' style='max-width: 400px;'><div class='mb-3'><label>ID детали</label><input type='number' name='partId' class='form-control' required/></div><div class='mb-3'><label>Количество</label><input type='number' name='quantity' class='form-control' required/></div><button type='submit' class='btn btn-success'>Оформить</button></form>");
        [HttpPost] public IActionResult PlaceOrder(int partId, int quantity) => _db.PlaceOrder(partId, quantity) ? Order("Заказ оформлен!", false) : Order("Ошибка! Проверьте ID или остаток.", true);

        // 5. Массовое поступление товара
        public IActionResult Restock(string? msg, bool isErr = false) => Html("Поступление", $"{(msg != null ? $"<div class='alert alert-{(isErr ? "danger" : "success")}'>{msg}</div>" : "")}<h2>5. Массовое поступление (Откат при ошибке)</h2><form method='post' action='/Parts/PostRestock' class='card p-4 bg-white mt-3' style='max-width: 500px;'><div class='row g-2 mb-2'><div class='col'><input type='number' name='ids' class='form-control' placeholder='ID детали 1' required/></div><div class='col'><input type='number' name='qtms' class='form-control' placeholder='Кол-во' required/></div></div><div class='row g-2 mb-2'><div class='col'><input type='number' name='ids' class='form-control' placeholder='ID детали 2' required/></div><div class='col'><input type='number' name='qtms' class='form-control' placeholder='Кол-во' required/></div></div><button type='submit' class='btn btn-warning mt-2'>Принять</button></form>");
        [HttpPost] public IActionResult PostRestock(List<int> ids, List<int> qtms) { var items = new List<(int, int)>(); for (int i = 0; i < ids.Count; i++) items.Add((ids[i], qtms[i])); return _db.Restock(items) ? Restock("Вся партия зачислена!", false) : Restock("Сбой! Один из ID не найден. Вся транзакция откатана.", true); }

        // 6. Сырое чтение
        public IActionResult Raw()
        {
            var data = _db.GetRawParts(); if (data.Count == 0) return Html("Reader", "<h2>6. Сырое чтение</h2><p>База пуста.</p>");
            var hd = new StringBuilder(); var rows = new StringBuilder(); var isHBuilt = false;
            foreach (var row in data)
            {
                if (!isHBuilt) { foreach (var key in row.Keys) hd.Append($"<th>{key}</th>"); isHBuilt = true; }
                rows.Append("<tr>"); foreach (var val in row.Values) rows.Append($"<td>{val ?? "NULL"}</td>"); rows.Append("</tr>");
            }
            return Html("Reader", $"<h2>6. Чтение через NpgsqlDataReader</h2><table class='table table-dark mt-3'><thead><tr>{hd}</tr></thead><tbody>{rows}</tbody></table>");
        }

        // 7. DataTable
        public IActionResult Tables()
        {
            var dt = _db.GetPartsDataTable(); var hd = new StringBuilder(); var rows = new StringBuilder();
            foreach (DataColumn col in dt.Columns) hd.Append($"<th>{col.ColumnName}</th>");
            foreach (DataRow row in dt.Rows) { rows.Append("<tr>"); foreach (var cell in row.ItemArray) rows.Append($"<td>{cell}</td>"); rows.Append("</tr>"); }
            return Html("DataTable", $"<h2>7. Вывод через DataTable</h2><table class='table table-bordered mt-3'><thead><tr>{hd}</tr></thead><tbody>{rows}</tbody></table>");
        }

        // 8. Асинхронный GetAllParts
        public async Task<IActionResult> Index()
        {
            var parts = await _db.GetAllPartsAsync(); var rows = new StringBuilder();
            foreach (var p in parts) rows.Append($"<tr><td>{p.Id}</td><td>{p.Name}</td><td>{p.Article}</td><td>{p.Price}</td><td>{p.Stock}</td></tr>");
            return Html("Async Index", $"<h2>8. Асинхронная версия (async/await)</h2><table class='table table-striped bg-white mt-3'><thead><tr><th>ID</th><th>Название</th><th>Артикул</th><th>Цена</th><th>Склад</th></tr></thead><tbody>{rows}</tbody></table>");
        }

        // 9. Ограничение UNIQUE
        public IActionResult Create(string? dbError = null) => Html("Добавление", $"{(dbError != null ? $"<div class='alert alert-danger'>{dbError}</div>" : "")}<h2>9. Обработка UNIQUE (SqlState 23505)</h2><form method='post' action='/Parts/PostCreate' class='card p-4 bg-white mt-3' style='max-width: 400px;'><div class='mb-2'><label>Название</label><input type='text' name='Name' class='form-control' required/></div><div class='mb-2'><label>Артикул</label><input type='text' name='Article' class='form-control' required/></div><div class='mb-2'><label>Цена</label><input type='number' step='0.01' name='Price' class='form-control' required/></div><div class='mb-3'><label>Количество</label><input type='number' name='Stock' class='form-control' required/></div><button type='submit' class='btn btn-primary'>Добавить</button></form>");
        [HttpPost] public IActionResult PostCreate(Part p) { var (ok, err) = _db.TryAddPart(p); return ok ? RedirectToAction(nameof(Search)) : Create(err); }

        // 10. Пул соединений
        public async Task<IActionResult> PoolTest()
        {
            var sw = Stopwatch.StartNew(); var tasks = new List<Task>();
            for (int i = 0; i < 20; i++) tasks.Add(Task.Run(async () => { await using var conn = new NpgsqlConnection("Host=localhost;Database=parts_db;Username=postgres;Password=1234;Maximum Pool Size=5;Minimum Pool Size=1"); await conn.OpenAsync(); await using var cmd = new NpgsqlCommand("SELECT 1; SELECT pg_sleep(0.04);", conn); await cmd.ExecuteScalarAsync(); }));
            await Task.WhenAll(tasks);
            sw.Stop();
            return Html("Тест пула", $"<h3>10. Тест Пула Соединений</h3><p>Задач параллельно: 20. Лимит пула: 5. Время выполнения: {sw.ElapsedMilliseconds} мс.</p>");
        }
    }
}

