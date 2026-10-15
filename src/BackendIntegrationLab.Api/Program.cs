using BackendIntegrationLab.Api.Data;
using BackendIntegrationLab.Api.Infrastructure;
using BackendIntegrationLab.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// Resolve the development database relative to this project, not the shell's directory.
// Integration tests replace this DbContext registration before the app is built.
var databasePath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "banking.db");
builder.Services.AddDbContext<BankingDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddScoped<BankingRepository>();
builder.Services.AddScoped<BankingService>();

var app = builder.Build();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// A new lab database is created automatically. EnsureCreated is deliberately simpler
// than migrations; delete the local database when experimenting with schema changes.
if (!app.Environment.IsEnvironment("Testing"))
    Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<BankingDbContext>();
    database.Database.EnsureCreated();
}

app.MapControllers();
app.Run();

// Makes the top-level entry point visible to WebApplicationFactory<Program>.
public partial class Program { }
