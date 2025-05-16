using System.ComponentModel.DataAnnotations;

namespace ProductionSystem.Models
{
    /// <summary>
    /// Модель детали - то что изготавливается на предприятии
    /// </summary>
    public class Detail
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(50)]
        public string Number { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Навигационные свойства
        public virtual ICollection<Operation> Operations { get; set; } = new List<Operation>();
        public virtual ICollection<ProductionOrder> ProductionOrders { get; set; } = new List<ProductionOrder>();
    }
}