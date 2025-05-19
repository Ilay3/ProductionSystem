using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Services;

var builder = WebApplication.CreateBuilder(args);

// Добавляем службы
builder.Services.AddControllersWithViews();

// Настройка подключения к базе данных
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=ProductionSystemDB;Username=postgres;Password=postgres";

builder.Services.AddDbContext<ProductionContext>(options =>
    options.UseNpgsql(connectionString,
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null)));

// Регистрация сервисов
builder.Services.AddScoped<IStageAssignmentService, StageAssignmentService>();
builder.Services.AddScoped<IStageAutomationService, StageAutomationService>();
builder.Services.AddScoped<IShiftService, ShiftService>();


// Регистрация фонового сервиса автоматизации
builder.Services.AddHostedService<StageAutomationBackgroundService>();

// Добавляем поддержку сессий
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Добавляем антифоргери токены
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

var app = builder.Build();

// Настройка pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseSession();
app.UseAuthorization();

// Настройка маршрутизации
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Автоматическое применение миграций при запуске
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ProductionContext>();
    try
    {
        context.Database.Migrate();
        Console.WriteLine("База данных успешно обновлена");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Ошибка при обновлении базы данных: {ex.Message}");
    }
}

app.Run();