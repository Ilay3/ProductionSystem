using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;

namespace ProductionSystem.Controllers
{
    public class MachinesController : Controller
    {
        private readonly ProductionContext _context;

        public MachinesController(ProductionContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var machines = _context.Machines.Include(m => m.MachineType);
            return View(await machines.ToListAsync());
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var machine = await _context.Machines
                .Include(m => m.MachineType)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (machine == null) return NotFound();

            return View(machine);
        }

        public IActionResult Create()
        {
            ViewData["MachineTypeId"] = new SelectList(_context.MachineTypes, "Id", "Name");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,InventoryNumber,MachineTypeId,Priority,Description")] Machine machine)
        {
            // Логирование для отладки
            Console.WriteLine($"Machine data received: Name={machine.Name}, InventoryNumber={machine.InventoryNumber}, MachineTypeId={machine.MachineTypeId}");

            // Проверяем состояние модели
            if (!ModelState.IsValid)
            {
                foreach (var error in ModelState)
                {
                    Console.WriteLine($"Validation error for {error.Key}: {string.Join(", ", error.Value.Errors.Select(e => e.ErrorMessage))}");
                }
            }

            // Проверка уникальности инвентарного номера
            if (await _context.Machines.AnyAsync(m => m.InventoryNumber == machine.InventoryNumber))
            {
                ModelState.AddModelError("InventoryNumber", "Станок с таким инвентарным номером уже существует");
            }

            if (ModelState.IsValid)
            {
                machine.CreatedAt = DateTime.UtcNow;
                _context.Add(machine);
                await _context.SaveChangesAsync();
                TempData["Message"] = "Станок успешно создан";
                return RedirectToAction(nameof(Index));
            }

            // Если валидация не прошла, выводим ошибки
            TempData["Error"] = "Проверьте правильность заполнения формы";
            ViewData["MachineTypeId"] = new SelectList(_context.MachineTypes, "Id", "Name", machine.MachineTypeId);
            return View(machine);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var machine = await _context.Machines.FindAsync(id);
            if (machine == null) return NotFound();

            ViewData["MachineTypeId"] = new SelectList(_context.MachineTypes, "Id", "Name", machine.MachineTypeId);
            return View(machine);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,InventoryNumber,MachineTypeId,Priority,Description")] Machine machine)
        {
            if (id != machine.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(machine);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!MachineExists(machine.Id))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["MachineTypeId"] = new SelectList(_context.MachineTypes, "Id", "Name", machine.MachineTypeId);
            return View(machine);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var machine = await _context.Machines
                .Include(m => m.MachineType)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (machine == null) return NotFound();

            return View(machine);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var machine = await _context.Machines.FindAsync(id);
            if (machine != null)
            {
                _context.Machines.Remove(machine);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private bool MachineExists(int id)
        {
            return _context.Machines.Any(e => e.Id == id);
        }
    }
}