using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace ProductionSystem.Models
{
    /// <summary>
    /// Модель производственного задания
    /// </summary>
    public class ProductionOrder
    {
        [Key]
        public int Id { get; set; }

        [StringLength(50)]
        public string Number { get; set; } = string.Empty; // Убираем [Required] - генерируется автоматически

        [Required]
        [ForeignKey("Detail")]
        public int DetailId { get; set; }

        /// <summary>
        /// Общее количество деталей в задании
        /// </summary>
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Количество должно быть больше 0")]
        public int TotalQuantity { get; set; }

        /// <summary>
        /// Статус задания
        /// </summary>
        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Created";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        [StringLength(500)]
        public string? Description { get; set; }

        // Навигационные свойства
        [ValidateNever]
        public virtual Detail Detail { get; set; } = null!;
        [ValidateNever]
        public virtual ICollection<SubBatch> SubBatches { get; set; } = new List<SubBatch>();
    }
}