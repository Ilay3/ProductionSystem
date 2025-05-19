using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Services;

var builder = WebApplication.CreateBuilder(args);

// ��������� ������
builder.Services.AddControllersWithViews();

// ��������� ����������� � ���� ������
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Database=ProductionSystemDB;Username=postgres;Password=postgres";

builder.Services.AddDbContext<ProductionContext>(options =>
    options.UseNpgsql(connectionString,
        npgsqlOptions => npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 3,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorCodesToAdd: null)));

// ����������� ��������
builder.Services.AddScoped<IStageAssignmentService, StageAssignmentService>();
builder.Services.AddScoped<IStageAutomationService, StageAutomationService>();
builder.Services.AddScoped<IShiftService, ShiftService>();


// ����������� �������� ������� �������������
builder.Services.AddHostedService<StageAutomationBackgroundService>();

// ��������� ��������� ������
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ��������� ����������� ������
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
});

var app = builder.Build();

// ��������� pipeline
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

// ��������� �������������
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// �������������� ���������� �������� ��� �������
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ProductionContext>();
    try
    {
        context.Database.Migrate();
        Console.WriteLine("���� ������ ������� ���������");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"������ ��� ���������� ���� ������: {ex.Message}");
    }
}

app.Run();