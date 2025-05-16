using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;

namespace ProductionSystem.Controllers
{
    public class GanttController : Controller
    {
        private readonly ProductionContext _context;

        public GanttController(ProductionContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(int? orderId, int? machineId)
        {
            ViewBag.Orders = await _context.ProductionOrders
                .Select(po => new { po.Id, po.Number, po.Detail.Name })
                .ToListAsync();

            ViewBag.Machines = await _context.Machines
                .Select(m => new { m.Id, m.Name })
                .ToListAsync();

            ViewBag.SelectedOrderId = orderId;
            ViewBag.SelectedMachineId = machineId;

            return View();
        }

        public async Task<IActionResult> MachineSchedule()
        {
            ViewBag.Machines = await _context.Machines
                .Select(m => new { m.Id, m.Name })
                .ToListAsync();

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> GetGanttData(int? orderId, int? machineId, DateTime? startDate, DateTime? endDate)
        {
            var query = _context.RouteStages
                .Include(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .Include(rs => rs.Machine)
                .Include(rs => rs.Operation)
                .Include(rs => rs.StageExecutions)
                .Where(rs => rs.Status != "Pending");

            if (orderId.HasValue)
            {
                query = query.Where(rs => rs.SubBatch.ProductionOrderId == orderId.Value);
            }

            if (machineId.HasValue)
            {
                query = query.Where(rs => rs.MachineId == machineId.Value);
            }

            var stages = await query.OrderBy(rs => rs.SubBatchId)
                .ThenBy(rs => rs.Order)
                .ToListAsync();

            var ganttData = stages.Select(stage => new
            {
                taskId = $"task_{stage.Id}",
                taskName = $"{stage.SubBatch.ProductionOrder.Detail.Name} - П{stage.SubBatch.BatchNumber} - {stage.Name}",
                resource = stage.Machine?.Name ?? "Не назначен",
                start = GetStageStartDate(stage),
                end = GetStageEndDate(stage),
                duration = (int)(stage.PlannedTime * 24 * 60 * 60 * 1000), // миллисекунды
                percentComplete = GetStageProgress(stage),
                dependencies = GetStageDependencies(stage),
                status = stage.Status,
                stageType = stage.StageType,
                actualStart = GetActualStartDate(stage),
                actualEnd = GetActualEndDate(stage),
                subBatchId = stage.SubBatchId,
                orderId = stage.SubBatch.ProductionOrderId,
                stageId = stage.Id
            }).ToList();

            return Json(ganttData);
        }

        private DateTime? GetStageStartDate(RouteStage stage)
        {
            // Если этап запущен, используем фактическую дату
            var execution = stage.StageExecutions.FirstOrDefault();
            if (execution?.StartedAt != null)
                return execution.StartedAt;

            // Если есть плановая дата
            if (stage.PlannedStartDate != null)
                return stage.PlannedStartDate;

            // Иначе вычисляем на основе предыдущих этапов
            return CalculateStageStartDate(stage);
        }

        private DateTime? GetStageEndDate(RouteStage stage)
        {
            var execution = stage.StageExecutions.FirstOrDefault();
            if (execution?.CompletedAt != null)
                return execution.CompletedAt;

            if (stage.PlannedEndDate != null)
                return stage.PlannedEndDate;

            var startDate = GetStageStartDate(stage);
            if (startDate.HasValue)
                return startDate.Value.AddHours((double)stage.PlannedTime);

            return null;
        }

        private DateTime? CalculateStageStartDate(RouteStage stage)
        {
            // Находим предыдущий этап
            var previousStage = _context.RouteStages
                .Where(rs => rs.SubBatchId == stage.SubBatchId && rs.Order < stage.Order)
                .OrderByDescending(rs => rs.Order)
                .FirstOrDefault();

            if (previousStage != null)
            {
                var prevEndDate = GetStageEndDate(previousStage);
                if (prevEndDate.HasValue)
                    return prevEndDate.Value;
            }

            // Если это первый этап, используем дату создания заказа
            return stage.SubBatch.ProductionOrder.CreatedAt;
        }

        private DateTime? GetActualStartDate(RouteStage stage)
        {
            return stage.StageExecutions.FirstOrDefault()?.StartedAt;
        }

        private DateTime? GetActualEndDate(RouteStage stage)
        {
            return stage.StageExecutions.FirstOrDefault()?.CompletedAt;
        }

        private int GetStageProgress(RouteStage stage)
        {
            return stage.Status switch
            {
                "Completed" => 100,
                "InProgress" => 50,
                "Paused" => 50,
                "Ready" => 0,
                _ => 0
            };
        }

        private string GetStageDependencies(RouteStage stage)
        {
            var previousStage = _context.RouteStages
                .Where(rs => rs.SubBatchId == stage.SubBatchId && rs.Order < stage.Order)
                .OrderByDescending(rs => rs.Order)
                .FirstOrDefault();

            return previousStage != null ? $"task_{previousStage.Id}" : "";
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStageDates(int stageId, DateTime startDate, DateTime endDate)
        {
            var stage = await _context.RouteStages.FindAsync(stageId);
            if (stage != null)
            {
                stage.PlannedStartDate = startDate;
                stage.PlannedEndDate = endDate;

                // Пересчитываем плановое время
                var timeSpan = endDate - startDate;
                stage.PlannedTime = (decimal)timeSpan.TotalHours;

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }

            return Json(new { success = false });
        }

        [HttpGet]
        public async Task<IActionResult> GetMachineUtilization(int? machineId, DateTime? startDate, DateTime? endDate)
        {
            var query = _context.StageExecutions
                .Include(se => se.RouteStage)
                .ThenInclude(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .Include(se => se.Machine)
                .Where(se => se.Status == "Completed");

            if (machineId.HasValue)
            {
                query = query.Where(se => se.MachineId == machineId.Value);
            }

            if (startDate.HasValue)
            {
                query = query.Where(se => se.StartedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(se => se.CompletedAt <= endDate.Value);
            }

            var executions = await query.ToListAsync();

            var utilizationData = executions
                .GroupBy(se => se.Machine?.Name ?? "Не назначен")
                .Select(g => new
                {
                    machine = g.Key,
                    totalTime = g.Sum(se => se.ActualTime ?? 0),
                    changeoverTime = g.Where(se => se.RouteStage.StageType == "Changeover")
                                      .Sum(se => se.ActualTime ?? 0),
                    productionTime = g.Where(se => se.RouteStage.StageType == "Operation")
                                      .Sum(se => se.ActualTime ?? 0),
                    completedStages = g.Count()
                })
                .OrderByDescending(x => x.totalTime)
                .ToList();

            return Json(utilizationData);
        }
    }
}