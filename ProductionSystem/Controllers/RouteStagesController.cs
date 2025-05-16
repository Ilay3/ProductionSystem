using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;
using ProductionSystem.Services;

namespace ProductionSystem.Controllers
{
    public class RouteStagesController : Controller
    {
        private readonly ProductionContext _context;
        private readonly IStageAssignmentService _stageAssignmentService;

        public RouteStagesController(ProductionContext context, IStageAssignmentService stageAssignmentService)
        {
            _context = context;
            _stageAssignmentService = stageAssignmentService;
        }

        public async Task<IActionResult> Index(int? subBatchId)
        {
            IQueryable<RouteStage> stages = _context.RouteStages
                .Include(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .Include(rs => rs.Operation)
                .Include(rs => rs.Machine);

            if (subBatchId.HasValue)
            {
                stages = stages.Where(rs => rs.SubBatchId == subBatchId.Value);

                var subBatch = await _context.SubBatches
                    .Include(sb => sb.ProductionOrder)
                    .ThenInclude(po => po.Detail)
                    .FirstOrDefaultAsync(sb => sb.Id == subBatchId.Value);

                ViewBag.SubBatch = subBatch;
            }

            return View(await stages.OrderBy(rs => rs.Order).ToListAsync());
        }

        public async Task<IActionResult> AssignMachine(int id)
        {
            var routeStage = await _context.RouteStages
                .Include(rs => rs.Operation)
                .ThenInclude(o => o.MachineType)
                .Include(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .FirstOrDefaultAsync(rs => rs.Id == id);

            if (routeStage == null) return NotFound();

            // Получаем подходящие станки
            var machineTypeId = routeStage.Operation?.MachineTypeId;
            var machines = await _context.Machines
                .Include(m => m.MachineType)
                .Where(m => machineTypeId == null || m.MachineTypeId == machineTypeId)
                .OrderBy(m => m.Priority)
                .ToListAsync();

            ViewBag.Machines = new SelectList(machines, "Id", "Name", routeStage.MachineId);
            return View(routeStage);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignMachine(int id, int machineId)
        {
            var success = await _stageAssignmentService.AssignStageToMachine(id, machineId);

            if (success)
            {
                TempData["Message"] = "Станок успешно назначен";
            }
            else
            {
                TempData["Error"] = "Ошибка при назначении станка";
            }

            var routeStage = await _context.RouteStages.FirstOrDefaultAsync(rs => rs.Id == id);
            return RedirectToAction(nameof(Index), new { subBatchId = routeStage?.SubBatchId });
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var routeStage = await _context.RouteStages
                .Include(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .Include(rs => rs.Operation)
                .Include(rs => rs.Machine)
                .ThenInclude(m => m.MachineType)
                .Include(rs => rs.StageExecutions)
                .ThenInclude(se => se.ExecutionLogs)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (routeStage == null) return NotFound();

            return View(routeStage);
        }

        [HttpPost]
        public async Task<IActionResult> UpdatePlannedTime(int id, decimal plannedTime)
        {
            var routeStage = await _context.RouteStages.FindAsync(id);
            if (routeStage != null)
            {
                routeStage.PlannedTime = plannedTime;
                await _context.SaveChangesAsync();
                TempData["Message"] = "Плановое время обновлено";
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int id, string status)
        {
            var routeStage = await _context.RouteStages.FindAsync(id);
            if (routeStage != null)
            {
                routeStage.Status = status;
                await _context.SaveChangesAsync();
                TempData["Message"] = "Статус обновлен";
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        public async Task<IActionResult> StartStage(int id, string? @operator)
        {
            var routeStage = await _context.RouteStages
                .Include(rs => rs.SubBatch)
                .FirstOrDefaultAsync(rs => rs.Id == id);

            if (routeStage == null || routeStage.Status != "Ready")
            {
                return Json(new { success = false, message = "Этап недоступен для запуска" });
            }

            // Проверяем, что предыдущий этап завершен
            var previousStage = await _context.RouteStages
                .Where(rs => rs.SubBatchId == routeStage.SubBatchId && rs.Order < routeStage.Order)
                .OrderByDescending(rs => rs.Order)
                .FirstOrDefaultAsync();

            if (previousStage != null && previousStage.Status != "Completed")
            {
                return Json(new { success = false, message = "Предыдущий этап не завершен" });
            }

            // Создаем выполнение этапа
            var execution = new StageExecution
            {
                RouteStageId = id,
                MachineId = routeStage.MachineId,
                Operator = @operator,
                Status = "Started",
                StartedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            _context.StageExecutions.Add(execution);
            routeStage.Status = "InProgress";

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Этап успешно запущен" });
        }

    }
}