using Microsoft.EntityFrameworkCore;
using Trading.Oms.Api.Contracts;
using Trading.Oms.Api.Oms.Domain.Interface;
using Trading.Oms.Api.Oms.Domain.Services;
using Trading.Oms.Application.Interfaces;
using Trading.Oms.Application.Services;
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

        string connString = builder.Configuration.GetConnectionString("OmsDatabase");

        // Add services to the container.
        builder.Services.AddControllers();

        // Add services to the container.
        builder.Services.AddAuthorization();

        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
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