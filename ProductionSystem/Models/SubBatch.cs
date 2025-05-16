using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductionSystem.Models
{
    /// <summary>
    /// Модель подпартии (части производственного задания)
    /// </summary>
    public class SubBatch
    {
        [Key]
        public int Id { get; set; }

        [ForeignKey("ProductionOrder")]
        public int ProductionOrderId { get; set; }

        /// <summary>
        /// Номер подпартии в рамках задания
        /// </summary>
        public int BatchNumber { get; set; }

        /// <summary>
        /// Количество деталей в этой подпартии
        /// </summary>
        public int Quantity { get; set; }

        /// <summary>
        /// Статус подпартии
        /// </summary>
        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Created";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        // Навигационные свойства
        public virtual ProductionOrder ProductionOrder { get; set; } = null!;
        public virtual ICollection<RouteStage> RouteStages { get; set; } = new List<RouteStage>();
    }
}