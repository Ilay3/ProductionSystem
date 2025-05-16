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
                "Ready" => "Готов",
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
    }
}