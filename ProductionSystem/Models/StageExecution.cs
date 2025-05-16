using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductionSystem.Models
{
    /// <summary>
    /// Модель выполнения этапа с фиксацией времени
    /// </summary>
    public class StageExecution
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("RouteStage")]
        public int RouteStageId { get; set; }

        [ForeignKey("Machine")]
        public int? MachineId { get; set; }

        /// <summary>
        /// Кто выполняет этап
        /// </summary>
        [StringLength(100)]
        public string? Operator { get; set; }

        /// <summary>
        /// Статус выполнения
        /// </summary>
        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Pending";

        public DateTime? StartedAt { get; set; }
        public DateTime? PausedAt { get; set; }
        public DateTime? ResumedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Общее время выполнения в часах (без учета пауз)
        /// </summary>
        [Column(TypeName = "decimal(8,4)")]
        public decimal? ActualTime { get; set; }

        /// <summary>
        /// Общее время пауз в часах
        /// </summary>
        [Column(TypeName = "decimal(8,4)")]
        public decimal? PauseTime { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        /// <summary>
        /// Причина превышения времени (если фактическое > планового)
        /// </summary>
        [StringLength(500)]
        public string? TimeExceededReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Навигационные свойства
        public virtual RouteStage RouteStage { get; set; } = null!;
        public virtual Machine? Machine { get; set; }
        public virtual ICollection<ExecutionLog> ExecutionLogs { get; set; } = new List<ExecutionLog>();
    }
}