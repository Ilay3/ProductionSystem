using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ProductionSystem.Data;
using ProductionSystem.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ProductionContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllersWithViews();

// Регистрируем сервисы
builder.Services.AddScoped<IStageAssignmentService, StageAssignmentService>();
builder.Services.AddScoped<IStageAutomationService, StageAutomationService>();

// Регистрируем фоновый сервис
builder.Services.AddHostedService<StageAutomationBackgroundService>();

// Добавляем логирование
builder.Services.AddLogging();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
        if (exceptionHandlerPathFeature?.Error != null)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(exceptionHandlerPathFeature.Error, "Необработанная ошибка");

            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Произошла внутренняя ошибка сервера",
                details = context.Request.Headers.ContainsKey("Accept") &&
                         context.Request.Headers["Accept"].ToString().Contains("application/json")
                    ? exceptionHandlerPathFeature.Error.Message
                    : null
            }));
        }
    });
});


app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();