// ProductionSystem/Controllers/ShiftsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;

namespace ProductionSystem.Controllers
{
    public class ShiftsController : Controller
    {
        private readonly ProductionContext _context;

        public ShiftsController(ProductionContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            return View(await _context.Shifts.ToListAsync());
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var shift = await _context.Shifts
                .Include(s => s.ShiftAssignments)
                .ThenInclude(sa => sa.Machine)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (shift == null) return NotFound();

            return View(shift);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Shift shift)
        {
            ModelState.Remove("ShiftAssignments");

            if (ModelState.IsValid)
            {
                shift.CreatedAt = ProductionContext.GetLocalNow();
                _context.Add(shift);
                await _context.SaveChangesAsync();
                TempData["Message"] = "Смена успешно создана";
                return RedirectToAction(nameof(Index));
            }
            return View(shift);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var shift = await _context.Shifts.FindAsync(id);
            if (shift == null) return NotFound();

            return View(shift);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Shift shift)
        {
            if (id != shift.Id) return NotFound();

            ModelState.Remove("ShiftAssignments");

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(shift);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ShiftExists(shift.Id))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(shift);
        }

        public async Task<IActionResult> AssignMachines(int? id)
        {
            if (id == null) return NotFound();

            var shift = await _context.Shifts.FindAsync(id);
            if (shift == null) return NotFound();

            var currentAssignments = await _context.ShiftAssignments
                .Where(sa => sa.ShiftId == id)
                .Select(sa => sa.MachineId)
                .ToListAsync();

            var machines = await _context.Machines.ToListAsync();

            ViewBag.Shift = shift;
            ViewBag.Machines = machines;
            ViewBag.CurrentAssignments = currentAssignments;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignMachines(int id, int[] selectedMachines)
        {
            var shift = await _context.Shifts.FindAsync(id);
            if (shift == null) return NotFound();

            // Удаляем существующие назначения
            var existingAssignments = await _context.ShiftAssignments
                .Where(sa => sa.ShiftId == id)
                .ToListAsync();

            _context.ShiftAssignments.RemoveRange(existingAssignments);

            // Добавляем новые назначения
            foreach (var machineId in selectedMachines)
            {
                _context.ShiftAssignments.Add(new ShiftAssignment
                {
                    ShiftId = id,
                    MachineId = machineId,
                    IsActive = true,
                    CreatedAt = ProductionContext.GetLocalNow()
                });
            }

            await _context.SaveChangesAsync();
            TempData["Message"] = "Назначения смены обновлены";
            return RedirectToAction(nameof(Details), new { id });
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var shift = await _context.Shifts
                .FirstOrDefaultAsync(m => m.Id == id);
            if (shift == null) return NotFound();

            return View(shift);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var shift = await _context.Shifts.FindAsync(id);
            if (shift != null)
            {
                _context.Shifts.Remove(shift);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private bool ShiftExists(int id)
        {
            return _context.Shifts.Any(e => e.Id == id);
        }
    }
}