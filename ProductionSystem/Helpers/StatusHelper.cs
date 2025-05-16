namespace ProductionSystem.Helpers
{
    public static class StatusHelper
    {
        public static string GetStatusDisplayName(string status)
        {
            return status switch
            {
                "Created" => "Создан",
                "InProgress" => "В работе",
                "Completed" => "Завершен",
                "Cancelled" => "Отменен",
                "Pending" => "Ожидает",
                "Ready" => "Готов к запуску",
                "Waiting" => "В очереди",
                "Paused" => "На паузе",
                "Started" => "Запущен",
                _ => status
            };
        }

        public static string GetStageTypeDisplayName(string stageType)
        {
            return stageType switch
            {
                "Operation" => "Операция",
                "Changeover" => "Переналадка",
                _ => stageType
            };
        }

        public static string GetStatusBadgeClass(string status)
        {
            return status switch
            {
                "Created" => "bg-secondary",
                "InProgress" => "bg-primary",
                "Completed" => "bg-success",
                "Cancelled" => "bg-danger",
                "Pending" => "bg-secondary",
                "Ready" => "bg-info",
                "Waiting" => "bg-warning",
                "Paused" => "bg-warning",
                "Started" => "bg-primary",
                _ => "bg-secondary"
            };
        }

        public static string GetStageTypeIconClass(string stageType)
        {
            return stageType switch
            {
                "Operation" => "fas fa-cog",
                "Changeover" => "fas fa-exchange-alt",
                _ => "fas fa-question"
            };
        }
    }
}