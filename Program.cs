using System.Text;
using AiEmailer.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddControllers();
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
builder.Services.AddScoped<IAttachmentTextExtractor, AttachmentTextExtractor>();
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

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        // Dev-only: the static frontend is served from file:// or a plain
        // http-server during development. Tighten this to your real origin
        // before deploying.
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
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

app.UseHttpsRedirection();
app.UseCors("Frontend");

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
