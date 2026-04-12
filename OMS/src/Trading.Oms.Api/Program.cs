using Microsoft.EntityFrameworkCore;
using Orderbook;
using Trading.Oms.Api.Oms.Domain.Interface;
using Trading.Oms.Api.Oms.Domain.Services;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Services;
using Trading.Oms.Infrastructure.Grpc;
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
        builder.Services.AddControllers();

        builder.Services.AddDbContext<OmsDbContext>(options => options.UseNpgsql(connString));
        builder.Services.AddScoped<IOrderSequenceAllocator, MockOrderSequenceAllocator>();
        builder.Services.AddScoped<IOrderIdComposer, OrderIdComposer>();
        builder.Services.AddScoped<IMatchingEngineClient, MockMatchingEngineClient>();
        builder.Services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
        builder.Services.AddScoped<IHashingService, Sha256HashingService>();
        builder.Services.AddScoped<IPlaceOrderCommandHandler, PlaceOrderCommandHandler>();
        builder.Services.AddScoped<ICancelOrderCommandHandler, CancelOrderCommandHandler>();
        builder.Services.AddScoped<ICommandAuditRepository, CommandAuditRepository>();
        builder.Services.AddGrpcClient<MatchingEngine.MatchingEngineClient>(o =>
        {
            o.Address = new Uri(engineUrl);
        });
        builder.Services.AddSingleton<IMatchingEngineClient, GrpcMatchingEngineClient>();

        var app = builder.Build();
        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        // app.UseHttpsRedirection();

        app.MapControllers();

        app.UseAuthorization();

        app.Run();
    }
}