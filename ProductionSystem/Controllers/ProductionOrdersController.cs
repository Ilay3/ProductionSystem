using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Models;

namespace ProductionSystem.Controllers
{
    public class ProductionOrdersController : Controller
    {
        private readonly ProductionContext _context;

        public ProductionOrdersController(ProductionContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var orders = _context.ProductionOrders
                .Include(p => p.Detail)
                .Include(p => p.SubBatches);
            return View(await orders.ToListAsync());
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.ProductionOrders
                .Include(p => p.Detail)
                .Include(p => p.SubBatches)
                .ThenInclude(s => s.RouteStages)
                .ThenInclude(r => r.Operation)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (order == null) return NotFound();

            return View(order);
        }

        public IActionResult Create()
        {
            ViewData["DetailId"] = new SelectList(_context.Details, "Id", "Name");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductionOrder order, int subBatchCount = 1)
        {
            // Генерируем номер заказа ДО валидации
            if (string.IsNullOrEmpty(order.Number))
            {
                order.Number = $"ПЗ-{DateTime.Now:yyyyMMdd}-{await _context.ProductionOrders.CountAsync() + 1:D3}";
            }

            // Удаляем ошибки валидации для навигационных свойств
            ModelState.Remove("Detail");
            ModelState.Remove("SubBatches");
            ModelState.Remove("Number"); // Убираем валидацию номера

            // Логирование для отладки
            Console.WriteLine($"ProductionOrder data: Detail={order.DetailId}, Quantity={order.TotalQuantity}, Number={order.Number}");

            if (!ModelState.IsValid)
            {
                foreach (var error in ModelState)
                {
                    Console.WriteLine($"Validation error for {error.Key}: {string.Join(", ", error.Value.Errors.Select(e => e.ErrorMessage))}");
                }
            }

            if (ModelState.IsValid)
            {
                order.Status = "Created";
                order.CreatedAt = DateTime.UtcNow;

                _context.Add(order);
                await _context.SaveChangesAsync();

                // Создание подпартий
                await CreateSubBatches(order, subBatchCount);

                TempData["Message"] = "Производственное задание успешно создано";
                return RedirectToAction(nameof(Details), new { id = order.Id });
            }

            TempData["Error"] = "Проверьте правильность заполнения формы";
            ViewData["DetailId"] = new SelectList(_context.Details, "Id", "Name", order.DetailId);
            return View(order);
        }

        private async Task CreateSubBatches(ProductionOrder order, int subBatchCount)
        {
            Console.WriteLine($"Creating {subBatchCount} sub-batches for order {order.Id}");

            var detail = await _context.Details
                .Include(d => d.Operations)
                .ThenInclude(o => o.MachineType)
                .FirstOrDefaultAsync(d => d.Id == order.DetailId);

            if (detail == null)
            {
                Console.WriteLine($"Detail with ID {order.DetailId} not found");
                return;
            }

            Console.WriteLine($"Found detail {detail.Name} with {detail.Operations.Count} operations");

            var quantityPerBatch = order.TotalQuantity / subBatchCount;
            var remainder = order.TotalQuantity % subBatchCount;

            for (int i = 1; i <= subBatchCount; i++)
            {
                var batchQuantity = quantityPerBatch + (i <= remainder ? 1 : 0);

                var subBatch = new SubBatch
                {
                    ProductionOrderId = order.Id,
                    BatchNumber = i,
                    Quantity = batchQuantity,
                    Status = "Created",
                    CreatedAt = DateTime.UtcNow
                };

                _context.SubBatches.Add(subBatch);
                await _context.SaveChangesAsync();

                Console.WriteLine($"Created sub-batch {i} with quantity {batchQuantity}");

                // Создание этапов маршрута для подпартии
                await CreateRouteStages(subBatch, detail);
            }
        }

        private async Task CreateRouteStages(SubBatch subBatch, Detail detail)
        {
            var operations = detail.Operations.OrderBy(o => o.Order).ToList();

            foreach (var operation in operations)
            {
                var routeStage = new RouteStage
                {
                    SubBatchId = subBatch.Id,
                    OperationId = operation.Id,
                    StageNumber = operation.OperationNumber,
                    Name = operation.Name,
                    StageType = "Operation",
                    Order = operation.Order,
                    PlannedTime = operation.TimePerPiece * subBatch.Quantity,
                    Quantity = subBatch.Quantity,
                    Status = "Pending"
                };

                _context.RouteStages.Add(routeStage);
            }

            await _context.SaveChangesAsync();
        }

        [HttpPost]
        public async Task<IActionResult> StartOrder(int id)
        {
            var order = await _context.ProductionOrders.FindAsync(id);
            if (order != null && order.Status == "Created")
            {
                order.Status = "InProgress";
                order.StartedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        public async Task<IActionResult> CompleteOrder(int id)
        {
            var order = await _context.ProductionOrders.FindAsync(id);
            if (order != null && order.Status == "InProgress")
            {
                order.Status = "Completed";
                order.CompletedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Details), new { id });
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.ProductionOrders
                .Include(p => p.Detail)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (order == null) return NotFound();

            return View(order);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var order = await _context.ProductionOrders.FindAsync(id);
            if (order != null)
            {
                _context.ProductionOrders.Remove(order);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}