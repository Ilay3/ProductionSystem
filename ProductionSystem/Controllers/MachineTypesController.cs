using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;

namespace ProductionSystem.Controllers
{
    public class MachineTypesController : Controller
    {
        private readonly ProductionContext _context;

        public MachineTypesController(ProductionContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            return View(await _context.MachineTypes.ToListAsync());
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var machineType = await _context.MachineTypes
                .Include(m => m.Machines)
                .Include(m => m.Operations)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (machineType == null) return NotFound();

            return View(machineType);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MachineType machineType)
        {
            // Удаляем ошибки валидации для навигационных свойств
            ModelState.Remove("Machines");
            ModelState.Remove("Operations");

            // Логирование для отладки
            Console.WriteLine($"MachineType data: Name={machineType.Name}");

            if (!ModelState.IsValid)
            {
                foreach (var error in ModelState)
                {
                    Console.WriteLine($"Validation error for {error.Key}: {string.Join(", ", error.Value.Errors.Select(e => e.ErrorMessage))}");
                }
            }

            if (ModelState.IsValid)
            {
                machineType.CreatedAt = DateTime.UtcNow;
                _context.Add(machineType);
                await _context.SaveChangesAsync();
                TempData["Message"] = "Тип станка успешно создан";
                return RedirectToAction(nameof(Index));
            }

            TempData["Error"] = "Проверьте правильность заполнения формы";
            return View(machineType);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var machineType = await _context.MachineTypes.FindAsync(id);
            if (machineType == null) return NotFound();

            return View(machineType);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Description")] MachineType machineType)
        {
            if (id != machineType.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(machineType);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!MachineTypeExists(machineType.Id))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(machineType);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var machineType = await _context.MachineTypes
                .FirstOrDefaultAsync(m => m.Id == id);
            if (machineType == null) return NotFound();

            return View(machineType);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var machineType = await _context.MachineTypes.FindAsync(id);
            if (machineType != null)
            {
                _context.MachineTypes.Remove(machineType);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private bool MachineTypeExists(int id)
        {
            return _context.MachineTypes.Any(e => e.Id == id);
        }
    }
}