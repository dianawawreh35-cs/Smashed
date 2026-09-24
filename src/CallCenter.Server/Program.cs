using CallCenter.Server.Data;
using CallCenter.Server.Data.Seed;
using CallCenter.Server.Features.Auth;
using CallCenter.Server.Features.Classifications;
using CallCenter.Server.Features.Communications;
using CallCenter.Server.Features.Contacts;
using CallCenter.Server.Features.Delivery;
using CallCenter.Server.Features.Menu;
using CallCenter.Server.Features.Settings;
using CallCenter.Server.Features.Users;
using CallCenter.Server.Hubs;
using CallCenter.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
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

    // ---- Authentication --------------------------------------------------
    // Bearer tokens for both clients. The signing key and the SIP secret key are
    // deployment secrets set in the server environment (runbook step 5).
    builder.Services.AddOptions<JwtOptions>()
        .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<TokenService>();
    builder.Services.AddSingleton<ISipSecretProtector, SipSecretProtector>();
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddScoped<UsersService>();
    builder.Services.AddScoped<ContactsService>();
    builder.Services.AddScoped<ContactFlagsService>();
    builder.Services.Configure<CallCenter.Server.Features.Communications.RecordingOptions>(
        builder.Configuration.GetSection(
            CallCenter.Server.Features.Communications.RecordingOptions.SectionName));
    builder.Services.AddSingleton<CallCenter.Server.Features.Communications.RecordingStore>();
    builder.Services.AddScoped<CommunicationsService>();
    builder.Services.AddScoped<CallCenter.Server.Features.Communications.RecordingRetention>();

    // A-33: the nightly pass that deletes recordings older than
    // recording.retention_days and keeps their rows. It reads the setting on
    // every run, so a change on the settings screen needs no restart.
    builder.Services.AddHostedService<CallCenter.Server.Workers.RecordingRetentionWorker>();
    builder.Services.AddScoped<CallEditWindow>();
    builder.Services.AddScoped<DeliveryAreasService>();
    builder.Services.AddOptions<MenuImageOptions>()
        .Bind(builder.Configuration.GetSection(MenuImageOptions.SectionName))
        .ValidateDataAnnotations();
    builder.Services.AddSingleton<MenuImageStore>();
    builder.Services.AddScoped<MenuService>();
    builder.Services.AddScoped<ClassificationService>();
    builder.Services.AddScoped<ClassificationTypeService>();
    builder.Services.AddScoped<SettingsService>();

    var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
              ?? throw new InvalidOperationException(
                  "The 'Jwt' configuration section is missing. See docs/DEPLOY-server-runbook.md step 5.");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = TokenService.CreateSigningKey(jwt.SigningKey),
                ValidateLifetime = true,
                // No grace period: a laptop with a badly set clock should fail
                // loudly at login rather than drift into odd behaviour later.
                ClockSkew = TimeSpan.FromSeconds(30),
            };

            // SignalR cannot set an Authorization header on the WebSocket
            // handshake, so the Agent App passes the token in the query string.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var token = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(token) &&
                        context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    {
                        context.Token = token;
                    }

                    return Task.CompletedTask;
                },
            };
        });

    builder.Services.AddAuthorizationBuilder()
        .AddPolicy(AuthPolicies.SignedIn, policy => policy.RequireAuthenticatedUser())
        .AddPolicy(AuthPolicies.SupervisorOnly, policy => policy.RequireRole(UserRoles.Supervisor))
        .AddPolicy(AuthPolicies.AgentOnly, policy => policy.RequireRole(UserRoles.Agent));

    // ---- Web ------------------------------------------------------------
    builder.Services.AddControllers();
    builder.Services.AddSignalR();
    builder.Services.AddProblemDetails();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new()
        {
            Title = "Restaurant Call Center API",
            Version = "v1",
            Description = "API for the Smashed Burger call centre. See docs/SRS-Smashed-Burger-Call-Center.md.",
        });

        // "Authorize" in the Swagger UI, so protected endpoints can be tried out.
        options.AddSecurityDefinition("Bearer", new()
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste the accessToken returned by POST /api/auth/login.",
        });

        options.AddSecurityRequirement(new()
        {
            [new() { Reference = new() { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
        });
    });

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

    builder.Services.AddScoped<DatabaseSeeder>();

    var app = builder.Build();

    // `dotnet CallCenter.Server.dll seed ...` - runbook step 7. Seeds and exits
    // rather than starting the web host.
    if (SeedCommand.IsRequested(args))
    {
        return await SeedCommand.RunAsync(app, args);
    }

    // `dotnet CallCenter.Server.dll reset-password ...` - the way back in when
    // the supervisor password is lost. Runs against the database and exits.
    if (ResetPasswordCommand.IsRequested(args))
    {
        return await ResetPasswordCommand.RunAsync(app.Services, args);
    }

    // Bring the schema up to date before serving. This is what makes the
    // runbook's "on first start the API applies database migrations" true.
    await DatabaseInitialiser.MigrateAsync(app.Services);

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
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapControllers();
    app.MapHub<AgentHub>("/hubs/agent");

    // SPA fallback: any non-API, non-hub GET returns index.html so client-side
    // routing works on refresh. Harmless when wwwroot is empty (404).
    app.MapFallbackToFile("index.html");

    Log.Information("Call Center API starting in {Environment}", app.Environment.EnvironmentName);
    await app.RunAsync();
    return 0;
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
