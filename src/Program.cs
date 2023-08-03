using FluentMigrator.Runner;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using ClickHouse.Client.ADO;
using ClickHouse.Client;
using Aptabase.Data;
using Aptabase.Data.Migrations;
using Aptabase.Features;
using Aptabase.Features.Privacy;
using Aptabase.Features.Query;
using Aptabase.Features.Blob;
using Aptabase.Features.GeoIP;
using Aptabase.Features.Ingestion;
using Aptabase.Features.Notification;
using Aptabase.Features.Authentication;
using Aptabase.Features.Billing.LemonSqueezy;
using Microsoft.AspNetCore.Server.Kestrel.Core;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;

            // Set to 1/4 of the default value to better support mobile devices on slow networks
            options.Limits.MinRequestBodyDataRate = new MinDataRate(bytesPerSecond: 60, gracePeriod: TimeSpan.FromSeconds(10));
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        var appEnv = EnvSettings.Load();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy(name: "AllowAptabaseCom", policy =>
            {
                policy.WithOrigins("https://aptabase.com")
                    .WithMethods("GET")
                    .AllowCredentials()
                    .SetPreflightMaxAge(TimeSpan.FromHours(1));
            });
            
            options.AddPolicy(name: "AllowAny", policy =>
            {
                policy.AllowAnyOrigin()
                    .WithHeaders("content-type", "app-key")
                    .WithMethods("POST")
                    .SetPreflightMaxAge(TimeSpan.FromHours(1));
            });
        });

        if (appEnv.IsManagedCloud)
        {
            builder.Services.AddDataProtection()
                            .PersistKeysToAWSSystemsManager("/aptabase/production/aspnet-dataprotection/");
        }

        builder.Services.AddControllers();
        builder.Services.AddResponseCaching();

        builder.Services.AddMemoryCache();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                        .AddCookie(options =>
                        {
                            options.ExpireTimeSpan = TimeSpan.FromDays(365);
                            options.Cookie.Name = "auth-session";
                            options.Cookie.SameSite = SameSiteMode.Strict;
                            options.Cookie.HttpOnly = true;
                            options.Cookie.IsEssential = true;
                            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                            options.Cookie.MaxAge = TimeSpan.FromDays(365);
                        }).AddGitHub(appEnv).AddGoogle(appEnv);

        builder.Services.AddRateLimiter(c =>
        {
            c.RejectionStatusCode = 429;

            c.AddPolicy("SignUp", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.ResolveClientIpAddress(),
                partition => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 4,
                    Window = TimeSpan.FromHours(1)
                })
            );

            c.AddPolicy("EventIngestion", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                httpContext.ResolveClientIpAddress(),
                partition => new FixedWindowRateLimiterOptions
                {
                    AutoReplenishment = true,
                    PermitLimit = 20,
                    Window = TimeSpan.FromSeconds(1)
                })
            );
        });

        builder.Services.AddSingleton(appEnv);
        builder.Services.AddHealthChecks();
        builder.Services.AddScoped<IAuthService, AuthService>();
        builder.Services.AddSingleton<IUserHashService, DailyUserHashService>();
        builder.Services.AddSingleton<IAuthTokenManager, AuthTokenManager>();
        builder.Services.AddSingleton<IIngestionValidator, IngestionValidator>();
        builder.Services.AddSingleton<IBlobService, DatabaseBlobService>();
        builder.Services.AddHostedService<PurgeDailySaltsCronJob>();
        builder.Services.AddGeoIPClient(appEnv);
        builder.Services.AddEmailClient(appEnv);
        builder.Services.AddLemonSqueezy(appEnv);

        if (!string.IsNullOrEmpty(appEnv.ClickHouseConnectionString))
        {
            builder.Services.AddSingleton<ClickHouseConnection>(x => new ClickHouseConnection(appEnv.ClickHouseConnectionString));
            builder.Services.AddSingleton<IClickHouseMigrationRunner, ClickHouseMigrationRunner>();
            builder.Services.AddSingleton<IQueryClient, ClickHouseQueryClient>();
            builder.Services.AddSingleton<IIngestionClient, ClickHouseIngestionClient>();
        }
        else
        {
            builder.Services.AddSingleton<IQueryClient, TinybirdQueryClient>();
            builder.Services.AddSingleton<IIngestionClient, TinybirdIngestionClient>();
            builder.Services.AddHttpClient("Tinybird", client =>
            {
                client.BaseAddress = new Uri(appEnv.TinybirdBaseUrl);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", appEnv.TinybirdToken);
            });
        }

        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        builder.Services.AddSingleton<IDbContext, DbContext>();
        builder.Services.AddNpgsqlDataSource(appEnv.ConnectionString);

        builder.Services.AddFluentMigratorCore().ConfigureRunner(
            r => r.AddPostgres()
                .WithGlobalConnectionString(appEnv.ConnectionString)
                .WithVersionTable(new VersionTable())
                .ScanIn(typeof(Program).Assembly).For.Migrations()
            );

        var app = builder.Build();

        if (appEnv.IsManagedCloud)
        {
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedProtoHeaderName = "Cloudfront-Forwarded-Proto",
                ForwardedHeaders = ForwardedHeaders.XForwardedProto
            });
        }
        else
        {
            app.UseForwardedHeaders();
        }

        app.MapHealthChecks("/healthz");
        app.UseMiddleware<ExceptionMiddleware>();
        app.UseRateLimiter();
        app.UseCors();
        app.UseAuthentication();
        app.MapControllers();
        app.UseResponseCaching();

        if (appEnv.IsProduction)
        {
            app.MapFallbackToFile("index.html", new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    ctx.Context.Response.Headers.Append("Cache-Control", "no-store,no-cache");
                }
            });

            app.UseStaticFiles(new StaticFileOptions
            {
                OnPrepareResponse = ctx =>
                {
                    if (ctx.Context.Request.Path.StartsWithSegments("/assets"))
                        ctx.Context.Response.Headers.Append("Cache-Control", "public,max-age=31536000,immutable");
                }
            });
        }

        RunMigrations(app.Services);

        app.Run();
    }

    public static void RunMigrations(IServiceProvider sp)
    {
        using (var scope = sp.CreateScope())
        {
            // Execute Postgres migrations
            var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();

            var env = scope.ServiceProvider.GetRequiredService<EnvSettings>();
            if (!string.IsNullOrEmpty(env.ClickHouseConnectionString))
            {
                // Execute ClickHouse migrations (if applicable)
                var chRunner = scope.ServiceProvider.GetRequiredService<IClickHouseMigrationRunner>();
                chRunner.MigrateUp();
            }
        }
    }
}