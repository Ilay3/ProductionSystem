using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;

namespace ProductionSystem.Models
{
    public class ShiftAssignment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey("Machine")]
        public int MachineId { get; set; }

        [Required]
        [ForeignKey("Shift")]
        public int ShiftId { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }

        // Навигационные свойства
        public virtual Machine Machine { get; set; } = null!;
        public virtual Shift Shift { get; set; } = null!;
    }
}
