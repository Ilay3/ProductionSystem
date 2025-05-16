using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;

namespace ProductionSystem.Controllers
{
    public class OperationsController : Controller
    {
        private readonly ProductionContext _context;

        public OperationsController(ProductionContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int? detailId)
        {
            IQueryable<Operation> operations = _context.Operations
                .Include(o => o.Detail)
                .Include(o => o.MachineType);

            if (detailId.HasValue)
            {
                operations = operations.Where(o => o.DetailId == detailId.Value);
                ViewBag.DetailName = _context.Details.Find(detailId)?.Name;
            }

            return View(await operations.OrderBy(o => o.DetailId).ThenBy(o => o.Order).ToListAsync());
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var operation = await _context.Operations
                .Include(o => o.Detail)
                .Include(o => o.MachineType)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (operation == null) return NotFound();

            return View(operation);
        }

        public IActionResult Create(int? detailId)
        {
            ViewData["DetailId"] = new SelectList(_context.Details, "Id", "Name", detailId);
            ViewData["MachineTypeId"] = new SelectList(_context.MachineTypes, "Id", "Name");

            var operation = new Operation();
            if (detailId.HasValue)
            {
                operation.DetailId = detailId.Value;
                var maxOrder = _context.Operations
                    .Where(o => o.DetailId == detailId)
                    .Select(o => o.Order)
                    .DefaultIfEmpty(0)
                    .Max();
                operation.Order = maxOrder + 10;
            }

            return View(operation);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Operation operation)
        {
            // Удаляем ошибки валидации для навигационных свойств
            ModelState.Remove("Detail");
            ModelState.Remove("MachineType");

            // Логирование для отладки
            Console.WriteLine($"Operation data: Detail={operation.DetailId}, Number={operation.OperationNumber}, Name={operation.Name}");

            if (!ModelState.IsValid)
            {
                foreach (var error in ModelState)
                {
                    Console.WriteLine($"Validation error for {error.Key}: {string.Join(", ", error.Value.Errors.Select(e => e.ErrorMessage))}");
                }
            }

            if (ModelState.IsValid)
            {
                operation.CreatedAt = DateTime.UtcNow;
                _context.Add(operation);
                await _context.SaveChangesAsync();
                TempData["Message"] = "Операция успешно создана";
                return RedirectToAction(nameof(Index), new { detailId = operation.DetailId });
            }

            TempData["Error"] = "Проверьте правильность заполнения формы";
            ViewData["DetailId"] = new SelectList(_context.Details, "Id", "Name", operation.DetailId);
            ViewData["MachineTypeId"] = new SelectList(_context.MachineTypes, "Id", "Name", operation.MachineTypeId);
            return View(operation);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var operation = await _context.Operations.FindAsync(id);
            if (operation == null) return NotFound();

            ViewData["DetailId"] = new SelectList(_context.Details, "Id", "Name", operation.DetailId);
            ViewData["MachineTypeId"] = new SelectList(_context.MachineTypes, "Id", "Name", operation.MachineTypeId);
            return View(operation);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,DetailId,OperationNumber,Name,MachineTypeId,TimePerPiece,Order,Description")] Operation operation)
        {
            if (id != operation.Id) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(operation);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!OperationExists(operation.Id))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction(nameof(Index), new { detailId = operation.DetailId });
            }
            ViewData["DetailId"] = new SelectList(_context.Details, "Id", "Name", operation.DetailId);
            ViewData["MachineTypeId"] = new SelectList(_context.MachineTypes, "Id", "Name", operation.MachineTypeId);
            return View(operation);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var operation = await _context.Operations
                .Include(o => o.Detail)
                .Include(o => o.MachineType)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (operation == null) return NotFound();

            return View(operation);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var operation = await _context.Operations.FindAsync(id);
            if (operation != null)
            {
                _context.Operations.Remove(operation);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }



        private bool OperationExists(int id)
        {
            return _context.Operations.Any(e => e.Id == id);
        }
    }
}