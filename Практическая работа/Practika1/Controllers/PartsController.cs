using Microsoft.AspNetCore.Mvc;
using AutoPartsApp.Data;
using AutoPartsApp.Models;

namespace AutoPartsApp.Controllers;

public class PartsController : Controller
{
    private readonly DbHelper _db;

    public PartsController(DbHelper db)
    {
        _db = db;
    }

    // GET /Parts
    public IActionResult Index()
    {
        var parts = _db.GetAllParts();
        return View(parts);
    }

    // GET /Parts/Details/5
    public IActionResult Details(int id)
    {
        var part = _db.GetPartById(id);
        if (part == null) return NotFound();
        return View(part);
    }

    // GET /Parts/Create
    public IActionResult Create() => View();

    // POST /Parts/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(Part part)
    {
        if (ModelState.IsValid)
        {
            _db.AddPart(part);
            return RedirectToAction(nameof(Index));
        }
        return View(part);
    }

    // POST /Parts/Buy/5
    [HttpPost]
    public IActionResult Buy(int id, int quantity = 1)
    {
        var success = _db.DecreaseStock(id, quantity);
        TempData["Message"] = success
            ? $"Запчасть куплена! Остаток уменьшен на {quantity}."
            : "Недостаточно запчастей на складе.";
        return RedirectToAction(nameof(Index));
    }
}