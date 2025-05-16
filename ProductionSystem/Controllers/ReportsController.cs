using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;

namespace ProductionSystem.Controllers
{
    public class ReportsController : Controller
    {
        private readonly ProductionContext _context;

        public ReportsController(ProductionContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> ProductionSummary(DateTime? startDate, DateTime? endDate)
        {
            var start = startDate?.Date ?? DateTime.Today.AddDays(-30);
            var end = endDate?.Date.AddDays(1) ?? DateTime.Today.AddDays(1);

            var startUtc = start.ToUniversalTime();
            var endUtc = end.ToUniversalTime();

            var executions = await _context.StageExecutions
                    .Include(se => se.RouteStage)
                    .ThenInclude(rs => rs.SubBatch)
                    .ThenInclude(sb => sb.ProductionOrder)
                    .ThenInclude(po => po.Detail)
                    .Include(se => se.Machine)
                    .Where(se => se.CompletedAt >= startUtc && se.CompletedAt <= endUtc && se.Status == "Completed")
                    .ToListAsync();


            var summary = new
            {
                DateFrom = startDate,
                DateTo = endDate,
                TotalOperations = executions.Count(se => se.RouteStage.StageType == "Operation"),
                TotalChangeovers = executions.Count(se => se.RouteStage.StageType == "Changeover"),
                TotalProductionTime = executions.Where(se => se.RouteStage.StageType == "Operation").Sum(se => se.ActualTime ?? 0),
                TotalChangeoverTime = executions.Where(se => se.RouteStage.StageType == "Changeover").Sum(se => se.ActualTime ?? 0),
                CompletedParts = executions.Where(se => se.RouteStage.StageType == "Operation").Sum(se => se.RouteStage.Quantity),
                MachineStats = executions.GroupBy(se => se.Machine?.Name ?? "Не назначен")
                    .Select(g => new
                    {
                        Machine = g.Key,
                        Operations = g.Count(se => se.RouteStage.StageType == "Operation"),
                        Changeovers = g.Count(se => se.RouteStage.StageType == "Changeover"),
                        ProductionTime = g.Where(se => se.RouteStage.StageType == "Operation").Sum(se => se.ActualTime ?? 0),
                        ChangeoverTime = g.Where(se => se.RouteStage.StageType == "Changeover").Sum(se => se.ActualTime ?? 0)
                    }).OrderByDescending(x => x.ProductionTime).ToList(),
                DetailStats = executions.Where(se => se.RouteStage.StageType == "Operation")
                    .GroupBy(se => se.RouteStage.SubBatch.ProductionOrder.Detail.Name)
                    .Select(g => new
                    {
                        Detail = g.Key,
                        Quantity = g.Sum(se => se.RouteStage.Quantity),
                        Operations = g.Count(),
                        TotalTime = g.Sum(se => se.ActualTime ?? 0),
                        AverageTime = g.Average(se => se.ActualTime ?? 0)
                    }).OrderByDescending(x => x.Quantity).ToList()
            };

            ViewBag.Summary = summary;
            return View();
        }

        public async Task<IActionResult> ChangeoverAnalysis(DateTime? startDate, DateTime? endDate)
        {
            startDate = (startDate ?? DateTime.Today.AddDays(-30)).ToUniversalTime();
            endDate = (endDate ?? DateTime.Today).ToUniversalTime().AddDays(1);

            var changeovers = await _context.StageExecutions
                .Include(se => se.RouteStage)
                .ThenInclude(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .Include(se => se.Machine)
                .Where(se => se.RouteStage.StageType == "Changeover" &&
                           se.CompletedAt >= startDate && se.CompletedAt <= endDate &&
                           se.Status == "Completed")
                .ToListAsync();

            var analysis = new
            {
                DateFrom = startDate,
                DateTo = endDate,
                TotalChangeovers = changeovers.Count,
                TotalChangeoverTime = changeovers.Sum(c => c.ActualTime ?? 0),
                AverageChangeoverTime = changeovers.Average(c => c.ActualTime ?? 0),
                ByMachine = changeovers.GroupBy(c => c.Machine?.Name ?? "Не назначен")
                    .Select(g => new
                    {
                        Machine = g.Key,
                        Count = g.Count(),
                        TotalTime = g.Sum(c => c.ActualTime ?? 0),
                        AverageTime = g.Average(c => c.ActualTime ?? 0)
                    }).OrderByDescending(x => x.TotalTime).ToList(),
                ByDetail = changeovers.GroupBy(c => c.RouteStage.SubBatch.ProductionOrder.Detail.Name)
                    .Select(g => new
                    {
                        Detail = g.Key,
                        Count = g.Count(),
                        TotalTime = g.Sum(c => c.ActualTime ?? 0),
                        AverageTime = g.Average(c => c.ActualTime ?? 0)
                    }).OrderByDescending(x => x.Count).ToList(),
                WorstChangeovers = changeovers.OrderByDescending(c => c.ActualTime)
                    .Take(10)
                    .Select(c => new
                    {
                        Machine = c.Machine?.Name ?? "Не назначен",
                        Detail = c.RouteStage.SubBatch.ProductionOrder.Detail.Name,
                        PlannedTime = c.RouteStage.PlannedTime,
                        ActualTime = c.ActualTime ?? 0,
                        Deviation = (c.ActualTime ?? 0) - c.RouteStage.PlannedTime,
                        Date = c.CompletedAt
                    }).ToList()
            };

            ViewBag.Analysis = analysis;
            return View();
        }

        public async Task<IActionResult> MachineEfficiency(DateTime? startDate, DateTime? endDate)
        {
            startDate = (startDate ?? DateTime.Today.AddDays(-30)).ToUniversalTime();
            endDate = (endDate ?? DateTime.Today).ToUniversalTime().AddDays(1);

            var machines = await _context.Machines
                .Include(m => m.StageExecutions.Where(se => se.CompletedAt >= startDate && se.CompletedAt <= endDate))
                .ThenInclude(se => se.RouteStage)
                .ToListAsync();

            var efficiency = machines.Select(m => new
            {
                Machine = m.Name,
                TotalOperations = m.StageExecutions.Count(se => se.RouteStage.StageType == "Operation"),
                PlannedTime = m.StageExecutions.Sum(se => se.RouteStage.PlannedTime),
                ActualTime = m.StageExecutions.Sum(se => se.ActualTime ?? 0),
                Efficiency = m.StageExecutions.Sum(se => se.RouteStage.PlannedTime) > 0 ?
                            (m.StageExecutions.Sum(se => se.RouteStage.PlannedTime) / m.StageExecutions.Sum(se => se.ActualTime ?? 1)) * 100 : 0,
                ChangeoverRatio = m.StageExecutions.Count() > 0 ?
                                (double)m.StageExecutions.Count(se => se.RouteStage.StageType == "Changeover") / m.StageExecutions.Count() * 100 : 0
            }).OrderByDescending(x => x.Efficiency).ToList();

            ViewBag.Efficiency = efficiency;
            ViewBag.StartDate = startDate;
            ViewBag.EndDate = endDate;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ExportProductionData(DateTime? startDate, DateTime? endDate, string format = "csv")
        {
            startDate = (startDate ?? DateTime.Today.AddDays(-30)).ToUniversalTime();
            endDate = (endDate ?? DateTime.Today).ToUniversalTime().AddDays(1);

            var executions = await _context.StageExecutions
                .Include(se => se.RouteStage)
                .ThenInclude(rs => rs.SubBatch)
                .ThenInclude(sb => sb.ProductionOrder)
                .ThenInclude(po => po.Detail)
                .Include(se => se.Machine)
                .Where(se => se.CompletedAt >= startDate && se.CompletedAt <= endDate && se.Status == "Completed")
                .OrderBy(se => se.CompletedAt)
                .ToListAsync();

            if (format.ToLower() == "csv")
            {
                var csv = "Дата,Задание,Деталь,Подпартия,Операция,Станок,Тип,Плановое время,Фактическое время,Количество,Оператор\n";

                foreach (var ex in executions)
                {
                    csv += $"{ex.CompletedAt?.ToString("dd.MM.yyyy HH:mm")}," +
                           $"{ex.RouteStage.SubBatch.ProductionOrder.Number}," +
                           $"{ex.RouteStage.SubBatch.ProductionOrder.Detail.Name}," +
                           $"{ex.RouteStage.SubBatch.BatchNumber}," +
                           $"{ex.RouteStage.Name}," +
                           $"{ex.Machine?.Name ?? "Не назначен"}," +
                           $"{ex.RouteStage.StageType}," +
                           $"{ex.RouteStage.PlannedTime:F2}," +
                           $"{ex.ActualTime:F2}," +
                           $"{ex.RouteStage.Quantity}," +
                           $"{ex.Operator ?? ""}\n";
                }

                var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
                return File(bytes, "text/csv", $"production-data-{startDate:yyyy-MM-dd}-{endDate:yyyy-MM-dd}.csv");
            }

            return BadRequest("Unsupported format");
        }
    }
}