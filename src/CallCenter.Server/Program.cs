using CallCenter.Server;
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
    // 127.0.0.1, not localhost: on Windows localhost tries IPv6 first, and every
    // new connection paid for the failed attempt (appsettings.Development.json).
    //
    // The pool is capped below PostgreSQL's own limit, so a burst of requests
    // cannot take every connection the database has (see ConnectionPool).
    var connectionString = ConnectionPool.WithMaxSize(
        builder.Configuration.GetConnectionString("Default")
            ?? "Host=127.0.0.1;Port=5432;Database=callcenter;Username=callcenter;Password=callcenter",
        builder.Configuration.GetValue("Database:MaxPoolSize", ConnectionPool.DefaultMaxSize));

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

    // F-13: the example keys from .env.example, and the development ones,
    // are public. Refused before anything starts, naming the setting to fill.
    DeploymentSecrets.Check(builder.Configuration, builder.Environment.IsDevelopment());

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<TokenService>();
    builder.Services.AddSingleton<ISipSecretProtector, SipSecretProtector>();
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddScoped<UsersService>();
    builder.Services.AddScoped<ContactCallLinker>();
    builder.Services.AddScoped<ContactsService>();
    builder.Services.AddScoped<ContactFlagsService>();
    builder.Services.Configure<CallCenter.Server.Features.Communications.RecordingOptions>(
        builder.Configuration.GetSection(
            CallCenter.Server.Features.Communications.RecordingOptions.SectionName));
    builder.Services.AddSingleton<CallCenter.Server.Features.Communications.RecordingStore>();
    builder.Services.AddScoped<CommunicationsService>();
    builder.Services.AddScoped<CallSearchService>();
    // A-70 to A-73: messages, the channels they arrive on, and their reports.
    builder.Services.AddScoped<CallCenter.Server.Features.Applications.ApplicationsService>();
    builder.Services.AddScoped<CallCenter.Server.Features.Channels.ChannelsService>();
    builder.Services.AddScoped<CallCenter.Server.Features.Reports.ApplicationReportsService>();
    builder.Services.AddScoped<CallCenter.Server.Features.Reports.CallReportsService>();
    builder.Services.AddScoped<CallCenter.Server.Features.Reports.ReportCube>();
    builder.Services.AddScoped<CallCenter.Server.Features.Communications.RecordingRetention>();

    // A-33: the nightly pass that deletes recordings older than
    // recording.retention_days and keeps their rows. It reads the setting on
    // every run, so a change on the settings screen needs no restart.
    builder.Services.AddHostedService<CallCenter.Server.Workers.RecordingRetentionWorker>();

    // A-67: every few minutes, ask the restaurant POS about recent callers
    // nobody has on file, and make or fill in their contacts. Off until
    // PosLookup:Token is set (POS_LOOKUP_TOKEN in .env).
    builder.Services.AddOptions<CallCenter.Server.Features.Pos.PosLookupOptions>()
        .Bind(builder.Configuration.GetSection(CallCenter.Server.Features.Pos.PosLookupOptions.SectionName));
    builder.Services.AddHttpClient<CallCenter.Server.Features.Pos.IPosCustomerLookup,
        CallCenter.Server.Features.Pos.PosCustomerClient>(http => http.Timeout = TimeSpan.FromSeconds(15));
    builder.Services.AddSingleton<CallCenter.Server.Features.Pos.PosLookupLedger>();
    builder.Services.AddScoped<CallCenter.Server.Features.Pos.PosCustomerSync>();
    builder.Services.AddHostedService<CallCenter.Server.Workers.PosLookupWorker>();

    // S-55: the abandoned calls, from the PBX's Calls Detail report, checked
    // every pbx.calls.interval_minutes. Off until the PBX's address and login
    // are entered on the settings screen.
    builder.Services.AddSingleton<CallCenter.Server.Features.Pbx.IPbxCallsDetailSource,
        CallCenter.Server.Features.Pbx.IssabelCallsClient>();
    builder.Services.AddSingleton<CallCenter.Server.Features.Pbx.AbandonedImportGate>();
    builder.Services.AddScoped<CallCenter.Server.Features.Pbx.AbandonedCallImport>();
    builder.Services.AddHostedService<CallCenter.Server.Workers.AbandonedCallImportWorker>();

    // S-46 and S-60: feature codes dialled from the server's own extension.
    // The PBX's blacklist follows the Blocked flag (*30 / *31), and the
    // supervisor opens and closes the queue (*280). Off until that extension is
    // entered on the settings screen.
    builder.Services.AddSingleton<CallCenter.Server.Features.Pbx.SipFeatureDialer>();
    builder.Services.AddSingleton<CallCenter.Server.Features.Pbx.IPbxFeatureDialer>(
        sp => sp.GetRequiredService<CallCenter.Server.Features.Pbx.SipFeatureDialer>());
    builder.Services.AddScoped<CallCenter.Server.Features.Pbx.PbxFeatureLine>();

    // S-61 and S-62: from the same extension, the server keeps the PBX telling
    // it what each agent's phone is doing, and a supervisor can listen in on a
    // call (*222 and the extension).
    builder.Services.AddSingleton<CallCenter.Server.Features.Pbx.IPbxCallListener>(
        sp => sp.GetRequiredService<CallCenter.Server.Features.Pbx.SipFeatureDialer>());
    builder.Services.AddSingleton<CallCenter.Server.Features.Pbx.ExtensionWatch>();
    builder.Services.AddSingleton<CallCenter.Server.Features.Pbx.ListenSessions>();
    builder.Services.AddScoped<CallCenter.Server.Features.Pbx.PbxListenService>();
    builder.Services.AddHostedService<CallCenter.Server.Workers.ExtensionWatchWorker>();
    builder.Services.AddSingleton<CallCenter.Server.Features.Pbx.PbxBlacklistGate>();
    builder.Services.AddScoped<CallCenter.Server.Features.Pbx.PbxBlacklistSync>();
    builder.Services.AddHostedService<CallCenter.Server.Workers.PbxBlacklistWorker>();
    builder.Services.AddSingleton<CallCenter.Server.Features.Pbx.PbxQueueGate>();
    builder.Services.AddScoped<CallCenter.Server.Features.Pbx.PbxQueueSwitch>();
    builder.Services.AddHostedService<CallCenter.Server.Workers.QueueAutoOpenWorker>();

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

    builder.Services.AddScoped<AccountTokenCheck>();

    // F-12: sign-in attempts are limited per address and per login. The limits
    // are configuration so the tests, which sign in hundreds of times a minute
    // from one address, can raise them.
    var loginLimits = builder.Configuration.GetSection(LoginLimitOptions.SectionName).Get<LoginLimitOptions>()
                      ?? new LoginLimitOptions();
    builder.Services.Configure<LoginLimitOptions>(builder.Configuration.GetSection(LoginLimitOptions.SectionName));
    builder.Services.AddSingleton<LoginThrottle>();
    builder.Services.AddRateLimiter(options => LoginThrottle.AddPolicy(options, loginLimits));

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

                // A token outlives nothing that happens to its account: a reset
                // password, a disable or a new role refuses it at once (N-05).
                OnTokenValidated = AccountTokenCheck.OnTokenValidatedAsync,
            };
        });

    // The fallback: an endpoint that names no policy needs a signed-in user
    // (27 Sep review), so a new controller cannot be anonymous by accident.
    // Sign-in, /health and the SPA's own files say AllowAnonymous. Every
    // controller action names its policy anyway (ControllerPolicyTests).
    builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build())
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

    // M-D04: /health is "the process is up"; /health/ready also asks the
    // database, and is what update.sh waits for.
    builder.Services.AddHealthChecks()
        .AddCheck<DatabaseHealthCheck>("database", tags: [DatabaseHealthCheck.Tag]);

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

    // M-D03: what refreshes itself on a timer is logged at Debug when it works.
    app.UseSerilogRequestLogging(options => options.GetLevel = RequestLogLevels.For);

    // Security headers on everything, the SPA's files included (27 Sep review).
    // Nothing here limits scripts or styles, which the web app decides; it
    // stops the pages being framed by another site, the browser guessing a
    // file's type, and the address leaking to other sites.
    app.Use((context, next) =>
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers.ContentSecurityPolicy = "frame-ancestors 'none'; object-src 'none'; base-uri 'self'";
        return next();
    });

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
    app.UseRateLimiter();
    app.UseAuthentication();
    app.Use(SessionPresence.StampAsync);
    app.UseAuthorization();

    app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => !check.Tags.Contains(DatabaseHealthCheck.Tag),
    }).AllowAnonymous();
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => true,
    }).AllowAnonymous();
    app.MapControllers();
    app.MapHub<AgentHub>("/hubs/agent");

    // SPA fallback: any non-API, non-hub GET returns index.html so client-side
    // routing works on refresh. Harmless when wwwroot is empty (404).
    app.MapFallbackToFile("index.html").AllowAnonymous();

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
