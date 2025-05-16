using ProductionSystem.Helpers;
using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace ProductionSystem.Models
{
    /// <summary>
    /// Модель этапа маршрута для подпартии
    /// </summary>
    public class RouteStage
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("SubBatch")]
        public int SubBatchId { get; set; }

        [ForeignKey("Operation")]
        public int? OperationId { get; set; }

        [ForeignKey("Machine")]
        public int? MachineId { get; set; }

        /// <summary>
        /// Номер этапа (например, 010, 015)
        /// </summary>
        [Required]
        [StringLength(10)]
        public string StageNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Тип этапа: Operation (обычная операция) или Changeover (переналадка)
        /// </summary>
        [Required]
        [StringLength(50)]
        public string StageType { get; set; } = "Operation";

        /// <summary>
        /// Порядок выполнения этапа
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// Плановое время выполнения в часах
        /// </summary>
        [Column(TypeName = "decimal(8,4)")]
        public decimal PlannedTime { get; set; }

        /// <summary>
        /// Количество деталей для обработки на этом этапе
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// Статус этапа: 
        /// Pending - ожидает запуска
        /// Ready - готов к запуску
        /// Waiting - в очереди ожидания
        /// InProgress - выполняется
        /// Paused - приостановлен
        /// Completed - завершен
        /// Cancelled - отменен
        /// </summary>
        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Pending";

        public DateTime? PlannedStartDate { get; set; }
        public DateTime? PlannedEndDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Навигационные свойства
        public virtual SubBatch SubBatch { get; set; } = null!;
        public virtual Operation? Operation { get; set; }
        public virtual Machine? Machine { get; set; }
        public virtual ICollection<StageExecution> StageExecutions { get; set; } = new List<StageExecution>();

        // Дополнительные свойства для удобства
        public string StatusDisplayName => StatusHelper.GetStatusDisplayName(Status);
        public string StageTypeDisplayName => StatusHelper.GetStageTypeDisplayName(StageType);

        /// <summary>
        /// Проверяет, может ли этап быть запущен
        /// </summary>
        public bool CanBeStarted => Status == "Ready" && MachineId.HasValue;

        /// <summary>
        /// Проверяет, в очереди ли этап
        /// </summary>
        public bool IsInQueue => Status == "Waiting";

        /// <summary>
        /// Проверяет, выполняется ли этап
        /// </summary>
        public bool IsInProgress => Status == "InProgress";

        /// <summary>
        /// Проверяет, завершен ли этап
        /// </summary>
        public bool IsCompleted => Status == "Completed";
    }
}