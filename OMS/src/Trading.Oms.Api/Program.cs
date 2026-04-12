using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orderbook;
using Trading.Oms.Api.Middleware;
using Trading.Oms.Domain.Interface;
using Trading.Oms.Domain.Services;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Services;
using Trading.Oms.Infrastructure.Grpc;
using Trading.Oms.Infrastructure.Health;
using Trading.Oms.Infrastructure.Persistence;
using Trading.Oms.Infrastructure.Repositories;
using Trading.Oms.Infrastructure.Services;
using Trading.Oms.Infrastructure.Services.Mock;

namespace Trading.Oms.Api;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var connString = builder.Configuration.GetConnectionString("OmsDatabase");
        var engineUrl = builder.Configuration.GetConnectionString("gRPCAddress");

        if (string.IsNullOrEmpty(connString) || string.IsNullOrEmpty(engineUrl))
        {
            throw new InvalidOperationException("Please set connection string or gRPC address.");
        }

        builder.Services.AddControllers();

        builder.Services.AddAuthorization();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Convert all the enum number to string 
        //  1 => Submitted, 2 => Rejected 
        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });

        builder.Services.AddDbContext<OmsDbContext>(options => options.UseNpgsql(connString));
        builder.Services.AddSingleton<IOrderSequenceAllocator, MockOrderSequenceAllocator>();
        builder.Services.AddScoped<IOrderIdComposer, OrderIdComposer>();
        builder.Services.AddScoped<IMatchingEngineClient, MockMatchingEngineClient>();
        builder.Services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        builder.Services.AddScoped<IHashingService, Sha256HashingService>();
        builder.Services.AddScoped<IPlaceOrderCommandHandler, PlaceOrderCommandHandler>();
        builder.Services.AddScoped<ICancelOrderCommandHandler, CancelOrderCommandHandler>();
        builder.Services.AddScoped<ICommandAuditRepository, CommandAuditRepository>();
        builder.Services.AddGrpcClient<MatchingEngine.MatchingEngineClient>(o => { o.Address = new Uri(engineUrl); });
        builder.Services.AddSingleton<IMatchingEngineClient, GrpcMatchingEngineClient>();
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
            .AddCheck<EngineHealthCheck>("engine", tags: new[] { "ready" });
        var app = builder.Build();
        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseMiddleware<GlobalExceptionMiddleware>();

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        });

        app.MapHealthChecks("/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var result = JsonSerializer.Serialize(new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(e => new { key = e.Key, status = e.Value.Status.ToString() })
                });
                await context.Response.WriteAsync(result);
            }
        });

        app.MapControllers();
        app.UseAuthorization();
        app.Run();
    }
}