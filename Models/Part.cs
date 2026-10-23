namespace AutoPartsApp.Models;

public class Part
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;        // Название запчасти
    public string Article { get; set; } = string.Empty;     // Артикул
    public string Manufacturer { get; set; } = string.Empty; // Производитель
    public decimal Price { get; set; }                      // Цена
    public int Stock { get; set; }                          // Остаток на складе
    public string? Description { get; set; }                // Описание
}