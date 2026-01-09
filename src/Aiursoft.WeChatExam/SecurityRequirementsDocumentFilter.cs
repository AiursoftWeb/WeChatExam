using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Aiursoft.WeChatExam;

public class SecurityRequirementsDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        if (!swaggerDoc.Components.SecuritySchemes.TryGetValue("BearerAuth", out var scheme))
        {
            return;
        }



        foreach (var pathItem in swaggerDoc.Paths.Values)
        {
            foreach (var operation in pathItem.Operations.Values)
            {
                if (operation.Security == null) continue;
                
                var requirementsToRemove = new List<OpenApiSecurityRequirement>();
                var requirementsToAdd = new List<OpenApiSecurityRequirement>();

                foreach (var requirement in operation.Security)
                {
                    var keys = requirement.Keys.ToList();
                    foreach (var key in keys)
                    {
                        // Check if it's the broken reference (checking type name string avoids namespace import issues)
                        if (key.GetType().Name == "OpenApiSecuritySchemeReference")
                        {
                            // Create a new valid reference using reflection
                            var ctor = typeof(Microsoft.OpenApi.OpenApiSecuritySchemeReference)
                                .GetConstructor(new[] { typeof(string), typeof(Microsoft.OpenApi.OpenApiDocument), typeof(string) });
                            
                            // Pass strict types properly
                            var validRef = (Microsoft.OpenApi.OpenApiSecuritySchemeReference)ctor.Invoke(new object[] { "BearerAuth", swaggerDoc, null });

                            var scopes = requirement[key];
                            
                            var newRequirement = new OpenApiSecurityRequirement
                            {
                                [validRef] = scopes
                            };
                            requirementsToAdd.Add(newRequirement);
                            requirementsToRemove.Add(requirement);
                        }
                    }
                }
                
                foreach(var req in requirementsToRemove)
                {
                    operation.Security.Remove(req);
                }
                foreach(var req in requirementsToAdd)
                {
                    operation.Security.Add(req);
                }
            }
        }
    }
}
