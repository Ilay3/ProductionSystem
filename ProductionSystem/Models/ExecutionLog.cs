using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductionSystem.Models
{
    /// <summary>
    /// Модель лога выполнения операций для отслеживания истории
    /// </summary>
    public class ExecutionLog
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("StageExecution")]
        public int StageExecutionId { get; set; }

        /// <summary>
        /// Тип действия: Started, Paused, Resumed, Completed, etc.
        /// </summary>
        [Required]
        [StringLength(50)]
        public string Action { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Operator { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [StringLength(500)]
        public string? Notes { get; set; }

        // Навигационные свойства
        public virtual StageExecution StageExecution { get; set; } = null!;
    }
}