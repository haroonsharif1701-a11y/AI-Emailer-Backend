using AiEmailer.Api.Data;
using AiEmailer.Api.Middlewares;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
// Local credentials stay outside Git; deployment environment variables take precedence.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);
var mailProviders = builder.Configuration.GetSection("MailProvider").Get<MailProviderSettings>() ?? new();
_ = ApplicationUrls.Origin(builder.Configuration, "BaseUrl");
_ = ApplicationUrls.Origin(builder.Configuration, "FrontendUrl");
mailProviders.Validate();
builder.Services.AddSingleton(mailProviders);
var protection = builder.Services.AddDataProtection().SetApplicationName("AiEmailer");
var keyPath = builder.Configuration["Mailbox:KeyPath"];
if (!string.IsNullOrWhiteSpace(keyPath))
{
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
    if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();
}
builder.Services.AddSingleton<MailboxTokenProtector>();
builder.Services.AddScoped<IMailboxRepository, MailboxRepository>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<GmailApiClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.MaxResponseContentBufferSize = 16 * 1024 * 1024;
}).RemoveAllLoggers();
builder.Services.AddScoped<IMailProvider, GmailMailProvider>();
builder.Services.AddScoped<MailboxService>();
builder.Services.AddScoped<MailboxAiService>();
builder.Services.AddHostedService<MailboxSyncWorker>();

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/app-log.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddDistributedMemoryCache();


builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax; // Changed from None to Lax for better compatibility with OAuth redirects
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // Changed from Always to SameAsRequest for local dev
});
// ---------------------------------------------------------------
// Config
// ---------------------------------------------------------------
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured. Set it via appsettings.json or user-secrets.");
var jwtIssuer = jwtSection["Issuer"] ?? "AiEmailer";
var jwtAudience = jwtSection["Audience"] ?? "AiEmailerClient";

// ---------------------------------------------------------------
// Services
// ---------------------------------------------------------------
builder.Services.AddControllers(options => options.Filters.Add<RbacExceptionFilter>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentAccessService, CurrentAccessService>();
builder.Services.AddScoped<ILoginSessionService, LoginSessionService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    var jwtScheme = new OpenApiSecurityScheme
    {
        Scheme = "bearer",
        BearerFormat = "JWT",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Description = "Paste ONLY the raw token here — Swagger adds the 'Bearer ' prefix for you.",
        Reference = new OpenApiReference
        {
            Id = JwtBearerDefaults.AuthenticationScheme,
            Type = ReferenceType.SecurityScheme,
        },
    };

    options.AddSecurityDefinition(jwtScheme.Reference.Id, jwtScheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { jwtScheme, Array.Empty<string>() },
    });
});

builder.Services.AddSingleton<IJwtTokenService>(new JwtTokenService(jwtKey, jwtIssuer, jwtAudience));
builder.Services.AddScoped<DB>();
builder.Services.AddScoped<IExternalAuthService, ExternalAuthService>();
builder.Services.AddScoped<IAttachmentTextExtractor, AttachmentTextExtractor>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
builder.Services.AddScoped<ITeamService, TeamService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<IRoleMenuPermissionService, RoleMenuPermissionService>();
builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
builder.Services.AddScoped<IAttachmentRepository, AttachmentRepository>();
// ILlmProvider is the abstraction the plan calls for — swap the registration
// below to switch providers without touching any controller or business logic.
var llmProvider = builder.Configuration["Llm:Provider"] ?? "Ollama";
if (llmProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<ILlmProvider, OpenAiLlmProvider>();
}
else
{
    builder.Services.AddHttpClient<ILlmProvider, OllamaLlmProvider>();
}
builder.Services.AddScoped<IAuthService, AuthService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
                .WithOrigins(new[] { ApplicationUrls.Origin(builder.Configuration, "FrontendUrl") }
                    .Concat(builder.Configuration.GetSection("Cors:AdditionalOrigins").Get<string[]>() ?? []).ToArray())
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});


var authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme =
        JwtBearerDefaults.AuthenticationScheme;

    options.DefaultChallengeScheme =
        JwtBearerDefaults.AuthenticationScheme;

    options.DefaultSignInScheme =
        CookieAuthenticationDefaults.AuthenticationScheme;
})
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,

            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey)),

            ClockSkew = TimeSpan.FromSeconds(30),
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal!;
                if (!Guid.TryParse(principal.FindFirst("jti")?.Value, out var sessionId) ||
                    !int.TryParse(principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value, out var userId) ||
                    !int.TryParse(principal.FindFirst("org_id")?.Value, out var orgId) ||
                    !await context.HttpContext.RequestServices.GetRequiredService<ILoginSessionService>()
                        .IsActiveAsync(sessionId, userId, orgId, context.HttpContext.RequestAborted))
                    context.Fail("Login session is expired or revoked.");
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();

                context.Response.StatusCode =
                    StatusCodes.Status401Unauthorized;

                context.Response.ContentType = "application/json";

                var message = "Authentication token is required.";

                if (context.AuthenticateFailure != null)
                {
                    message = context.AuthenticateFailure switch
                    {
                        SecurityTokenExpiredException =>
                            "Authentication token has expired.",

                        SecurityTokenMalformedException =>
                            "Authentication token is malformed.",

                        _ =>
                            "Authentication token is invalid."
                    };
                }

                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    statusCode = 401,
                    message
                });
            },

            OnForbidden = async context =>
            {
                context.Response.StatusCode =
                    StatusCodes.Status403Forbidden;

                context.Response.ContentType = "application/json";

                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    statusCode = 403,
                    message = "You do not have permission to access this resource."
                });
            }
        };
    });
if (mailProviders.Gmail) authentication.AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
        options.Events = new OAuthEvents
        {
              OnRedirectToAuthorizationEndpoint = context =>
              {
                  var redirect = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(context.RedirectUri).Query);
                  redirect["redirect_uri"] = ApplicationUrls.Origin(builder.Configuration, "BaseUrl") + "/signin-google";
                  context.Response.Redirect(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
                      context.RedirectUri.Split('?')[0], redirect));
                  return Task.CompletedTask;
              },
            OnRemoteFailure = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();
                logger.LogError("Google remote authentication failed.");

                context.HandleResponse();
                var frontendUrl = ApplicationUrls.LoginCallback(builder.Configuration);
                context.Response.Redirect($"{frontendUrl}?error=remote_failure");
                return Task.CompletedTask;
            }
        };
    });
if (mailProviders.Outlook) authentication.AddOpenIdConnect("EntraID", options =>
    {
        options.Authority = builder.Configuration["Authentication:EntraID:Instance"] + builder.Configuration["Authentication:EntraID:TenantId"] + "/v2.0";
        options.ClientId = builder.Configuration["Authentication:EntraID:ClientId"];
        options.ClientSecret = builder.Configuration["Authentication:EntraID:ClientSecret"];
        options.ResponseType = "code";
        options.SaveTokens = true;
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// ---------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

//app.UseHttpsRedirection(); //while pushing on server with a non http site then comment it
app.UseCors("Frontend");
app.UseSession();
app.UseMiddleware<MailProviderMiddleware>();
app.UseMiddleware<SystemKeyMiddleware>();
// Logs every request that hits the API — method, path, status code, and how
// long it took. This is what lets you SEE that CORS/auth/routing worked,
// instead of only finding out when something silently fails.
app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    await next();
    sw.Stop();
    logger.LogInformation(
        "{Method} {Path} -> {StatusCode} ({ElapsedMs}ms)",
        context.Request.Method, context.Request.Path, context.Response.StatusCode, sw.ElapsedMilliseconds);
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
