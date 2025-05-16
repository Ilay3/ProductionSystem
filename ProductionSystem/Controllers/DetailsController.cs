using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;

namespace ProductionSystem.Controllers
{
    public class DetailsController : Controller
    {
        private readonly ProductionContext _context;

        public DetailsController(ProductionContext context)
        {
            _context = context;
        }

        // GET: Details
        public async Task<IActionResult> Index()
        {
            return View(await _context.Details.ToListAsync());
        }

        // GET: Details/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var detail = await _context.Details
                .Include(d => d.Operations)
                .ThenInclude(o => o.MachineType)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (detail == null) return NotFound();

            return View(detail);
        }

        // GET: Details/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Details/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Detail detail)
        {
            // Удаляем ошибки валидации для навигационных свойств
            ModelState.Remove("Operations");
            ModelState.Remove("ProductionOrders");

            // Логирование для отладки
            Console.WriteLine($"Detail data: Name={detail.Name}, Number={detail.Number}");

            if (!ModelState.IsValid)
            {
                foreach (var error in ModelState)
                {
                    Console.WriteLine($"Validation error for {error.Key}: {string.Join(", ", error.Value.Errors.Select(e => e.ErrorMessage))}");
                }
            }

            // Проверка уникальности номера детали
            if (!string.IsNullOrEmpty(detail.Number) &&
                await _context.Details.AnyAsync(d => d.Number == detail.Number))
            {
                ModelState.AddModelError("Number", "Деталь с таким номером уже существует");
            }

            if (ModelState.IsValid)
            {
                detail.CreatedAt = DateTime.UtcNow;
                _context.Add(detail);
                await _context.SaveChangesAsync();
                TempData["Message"] = "Деталь успешно создана";
                return RedirectToAction(nameof(Index));
            }

            TempData["Error"] = "Проверьте правильность заполнения формы";
            return View(detail);
        }

        // GET: Details/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var detail = await _context.Details.FindAsync(id);
            if (detail == null) return NotFound();

            return View(detail);
        }

        // POST: Details/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Number,Description")] Detail detail)
        {
            if (id != detail.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(detail);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!DetailExists(detail.Id))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(detail);
        }

        // GET: Details/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var detail = await _context.Details
                .FirstOrDefaultAsync(m => m.Id == id);
            if (detail == null) return NotFound();

            return View(detail);
        }

        // POST: Details/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var detail = await _context.Details.FindAsync(id);
            if (detail != null)
            {
                _context.Details.Remove(detail);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private bool DetailExists(int id)
        {
            return _context.Details.Any(e => e.Id == id);
        }
    }
}