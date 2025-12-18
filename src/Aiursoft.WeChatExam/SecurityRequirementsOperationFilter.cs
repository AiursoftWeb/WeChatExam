using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Aiursoft.WeChatExam;

/// <summary>
/// Swagger operation filter that adds security requirements only to endpoints that require authentication.
/// Endpoints decorated with [AllowAnonymous] will not show the lock icon.
/// </summary>
public class SecurityRequirementsOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Check if the endpoint has [AllowAnonymous] attribute
        var hasAllowAnonymous = context.MethodInfo.DeclaringType != null &&
            (context.MethodInfo.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any() ||
             context.MethodInfo.DeclaringType.GetCustomAttributes(true).OfType<AllowAnonymousAttribute>().Any());

        // If the endpoint allows anonymous access, don't add security requirements
        if (hasAllowAnonymous)
        {
            return;
        }

        // Check if the endpoint has [Authorize] attribute (either on method or controller)
        var hasAuthorize = context.MethodInfo.DeclaringType != null &&
            (context.MethodInfo.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any() ||
             context.MethodInfo.DeclaringType.GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any());

        // Add security requirement only if the endpoint requires authorization
        if (hasAuthorize)
        {
            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        Array.Empty<string>()
                    }
                }
            };
        }
    }
}
