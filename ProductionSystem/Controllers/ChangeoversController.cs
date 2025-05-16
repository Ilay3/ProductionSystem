using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;

namespace ProductionSystem.Controllers
{
    public class ChangeoversController : Controller
    {
        private readonly ProductionContext _context;

        public ChangeoversController(ProductionContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var changeovers = _context.Changeovers
                .Include(c => c.Machine)
                .Include(c => c.FromDetail)
                .Include(c => c.ToDetail);
            return View(await changeovers.ToListAsync());
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var changeover = await _context.Changeovers
                .Include(c => c.Machine)
                .Include(c => c.FromDetail)
                .Include(c => c.ToDetail)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (changeover == null) return NotFound();

            return View(changeover);
        }

        public IActionResult Create()
        {
            ViewData["MachineId"] = new SelectList(_context.Machines, "Id", "Name");
            ViewData["FromDetailId"] = new SelectList(_context.Details, "Id", "Name");
            ViewData["ToDetailId"] = new SelectList(_context.Details, "Id", "Name");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Changeover changeover)
        {
            // Удаляем ошибки валидации для навигационных свойств
            ModelState.Remove("Machine");
            ModelState.Remove("FromDetail");
            ModelState.Remove("ToDetail");

            // Логирование для отладки
            Console.WriteLine($"Changeover data: Machine={changeover.MachineId}, From={changeover.FromDetailId}, To={changeover.ToDetailId}");

            if (!ModelState.IsValid)
            {
                foreach (var error in ModelState)
                {
                    Console.WriteLine($"Validation error for {error.Key}: {string.Join(", ", error.Value.Errors.Select(e => e.ErrorMessage))}");
                }
            }

            // Проверка на то, что FromDetail и ToDetail разные
            if (changeover.FromDetailId == changeover.ToDetailId)
            {
                ModelState.AddModelError("ToDetailId", "Детали 'откуда' и 'куда' должны быть разными");
            }

            // Проверка уникальности комбинации
            if (await _context.Changeovers.AnyAsync(c => c.MachineId == changeover.MachineId &&
                                                          c.FromDetailId == changeover.FromDetailId &&
                                                          c.ToDetailId == changeover.ToDetailId))
            {
                ModelState.AddModelError("", "Переналадка для этой комбинации уже существует");
            }

            if (ModelState.IsValid)
            {
                changeover.CreatedAt = DateTime.UtcNow;
                _context.Add(changeover);
                await _context.SaveChangesAsync();
                TempData["Message"] = "Переналадка успешно создана";
                return RedirectToAction(nameof(Index));
            }

            TempData["Error"] = "Проверьте правильность заполнения формы";
            ViewData["MachineId"] = new SelectList(_context.Machines, "Id", "Name", changeover.MachineId);
            ViewData["FromDetailId"] = new SelectList(_context.Details, "Id", "Name", changeover.FromDetailId);
            ViewData["ToDetailId"] = new SelectList(_context.Details, "Id", "Name", changeover.ToDetailId);
            return View(changeover);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var changeover = await _context.Changeovers.FindAsync(id);
            if (changeover == null) return NotFound();

            ViewData["MachineId"] = new SelectList(_context.Machines, "Id", "Name", changeover.MachineId);
            ViewData["FromDetailId"] = new SelectList(_context.Details, "Id", "Name", changeover.FromDetailId);
            ViewData["ToDetailId"] = new SelectList(_context.Details, "Id", "Name", changeover.ToDetailId);
            return View(changeover);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,MachineId,FromDetailId,ToDetailId,ChangeoverTime,Description")] Changeover changeover)
        {
            if (id != changeover.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(changeover);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ChangeoverExists(changeover.Id))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["MachineId"] = new SelectList(_context.Machines, "Id", "Name", changeover.MachineId);
            ViewData["FromDetailId"] = new SelectList(_context.Details, "Id", "Name", changeover.FromDetailId);
            ViewData["ToDetailId"] = new SelectList(_context.Details, "Id", "Name", changeover.ToDetailId);
            return View(changeover);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var changeover = await _context.Changeovers
                .Include(c => c.Machine)
                .Include(c => c.FromDetail)
                .Include(c => c.ToDetail)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (changeover == null) return NotFound();

            return View(changeover);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var changeover = await _context.Changeovers.FindAsync(id);
            if (changeover != null)
            {
                _context.Changeovers.Remove(changeover);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private bool ChangeoverExists(int id)
        {
            return _context.Changeovers.Any(e => e.Id == id);
        }
    }
}