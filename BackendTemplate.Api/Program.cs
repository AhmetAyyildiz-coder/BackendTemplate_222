using BackendTemplate.Api.Services;
using BackendTemplate.Domain.Sample;
using BackendTemplate.Persistence.DbContexts;
using BackendTemplate.Persistence.Interceptors;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<RequestContextAccessor>();

builder.Services.AddSingleton<AuditOutboxSaveChangesInterceptor>(sp =>
{
    var ctx = sp.GetRequiredService<RequestContextAccessor>();
    return new AuditOutboxSaveChangesInterceptor(ctx.GetTraceId, ctx.GetUserId);
});

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var interceptor = sp.GetRequiredService<AuditOutboxSaveChangesInterceptor>();
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("MainDb"))
        .AddInterceptors(interceptor);
});

builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("AuditDb")));

builder.Services.AddHangfire(config =>
    config
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(o =>
            o.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Hangfire")),
            new PostgreSqlStorageOptions
            {
                PrepareSchemaIfNecessary = true,
                QueuePollInterval = TimeSpan.FromSeconds(5)
            }));

builder.Services.AddHangfireServer();
builder.Services.AddScoped<AuditOutboxProcessor>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHangfireDashboard("/hangfire");

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

app.MapPost("/orders", async (AppDbContext db, CreateOrderRequest request, CancellationToken ct) =>
{
    var order = new Order
    {
        Id = Guid.NewGuid(),
        TotalAmount = request.TotalAmount,
        Currency = request.Currency
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/orders/{order.Id}", new { order.Id });
}).WithOpenApi();

app.MapPut("/orders/{id:guid}", async (AppDbContext db, Guid id, UpdateOrderRequest request, CancellationToken ct) =>
{
    var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (order is null)
        return Results.NotFound();

    order.TotalAmount = request.TotalAmount;
    order.Currency = request.Currency;

    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).WithOpenApi();

// Run the outbox processor as a recurring job (default: every 10 seconds).
var cron = app.Configuration["AuditOutbox:Cron"] ?? "*/10 * * * * *";
var batchSize = int.TryParse(app.Configuration["AuditOutbox:BatchSize"], out var bs) ? bs : 100;

RecurringJob.AddOrUpdate<AuditOutboxProcessor>(
    "audit-outbox",
    p => p.ProcessBatchAsync(batchSize, CancellationToken.None),
    cron);

app.Run();

sealed record CreateOrderRequest(decimal TotalAmount, string Currency);

sealed record UpdateOrderRequest(decimal TotalAmount, string Currency);

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
