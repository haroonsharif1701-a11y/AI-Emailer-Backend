using System.Security.Cryptography;
using System.Text;

namespace AiEmailer.Api.Middlewares
{
    public sealed class SystemKeyMiddleware
    {
        private const string HeaderName = "X-System-Key";

        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;

        public SystemKeyMiddleware(
            RequestDelegate next,
            IConfiguration configuration)
        {
            _next = next;
            _configuration = configuration;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Login is still protected by System Key, but not JWT.
            // If you want login to skip System Key too, add the path here.
            var configuredKey = _configuration["SystemKey:Key"];

            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                await WriteError(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "System key is not configured.");

                return;
            }

            if (!context.Request.Headers.TryGetValue(
                    HeaderName,
                    out var suppliedKey))
            {
                await WriteError(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "System key is required.");

                return;
            }

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(suppliedKey.ToString()),
                    Encoding.UTF8.GetBytes(configuredKey)))
            {
                await WriteError(
                    context,
                    StatusCodes.Status401Unauthorized,
                    "Invalid system key.");

                return;
            }

            await _next(context);
        }

        private static async Task WriteError(
            HttpContext context,
            int statusCode,
            string message)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(new
            {
                success = false,
                statusCode,
                message
            });
        }
    }
}
