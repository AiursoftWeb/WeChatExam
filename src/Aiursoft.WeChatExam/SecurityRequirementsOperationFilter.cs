using Microsoft.AspNetCore.Authorization;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Collections.Generic;
using System.Linq;

namespace Aiursoft.WeChatExam;

public class SecurityRequirementsOperationFilter : IOperationFilter
{
    public void Apply(Microsoft.OpenApi.OpenApiOperation operation, OperationFilterContext context)
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
            operation.Security = new List<Microsoft.OpenApi.OpenApiSecurityRequirement>
            {
                new Microsoft.OpenApi.OpenApiSecurityRequirement
                {
                    {
                        new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer"),
                        new List<string>()
                    }
                }
            };
        }
    }
}
