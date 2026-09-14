using CallCenter.Server.Data;
using CallCenter.Server.Hubs;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Bootstrap logger: captures anything that fails before configuration is read.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // ---- Database -------------------------------------------------------
    // The context is empty for now; entities and migrations arrive with the
    // schema work in the next prompt (docs/SCHEMA.md).
    var connectionString = builder.Configuration.GetConnectionString("Default")
                           ?? "Host=localhost;Port=5432;Database=callcenter;Username=callcenter;Password=callcenter";

    builder.Services.AddDbContext<CallCenterDbContext>(options =>
    {
        options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure());
        if (builder.Environment.IsDevelopment())
        {
            options.EnableDetailedErrors();
            options.EnableSensitiveDataLogging();
        }
    });

    // ---- Web ------------------------------------------------------------
    builder.Services.AddControllers();
    builder.Services.AddSignalR();
    builder.Services.AddProblemDetails();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new()
    {
        Title = "Restaurant Call Center API",
        Version = "v1",
        Description = "API for the Smashed Burger call centre. See docs/SRS-Smashed-Burger-Call-Center.md.",
    }));

    builder.Services.AddHealthChecks();

    // CORS for the Vite dev server. In production the SPA is served from
    // wwwroot by this same host, so no cross-origin request happens.
    const string DevCorsPolicy = "vite-dev";
    var devOrigins = builder.Configuration.GetSection("Cors:DevOrigins").Get<string[]>()
                     ?? new[] { "http://localhost:5173", "http://127.0.0.1:5173" };

    builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy => policy
        .WithOrigins(devOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
        app.UseSwagger();
        app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Call Center API v1"));
        app.UseCors(DevCorsPolicy);
    }
    else
    {
        app.UseExceptionHandler();
    }

    // Serve the built SPA out of wwwroot (populated by the Docker build).
    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseRouting();
    app.UseAuthorization();

    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapControllers();
    app.MapHub<AgentHub>("/hubs/agent");

    // SPA fallback: any non-API, non-hub GET returns index.html so client-side
    // routing works on refresh. Harmless when wwwroot is empty (404).
    app.MapFallbackToFile("index.html");

    Log.Information("Call Center API starting in {Environment}", app.Environment.EnvironmentName);
    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Call Center API terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can boot the API in tests.</summary>
public partial class Program;
