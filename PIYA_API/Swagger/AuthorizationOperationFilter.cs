using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PIYA_API.Swagger;

/// <summary>
/// An operation-level empty security requirement object overrides the
/// document-level Bearer requirement for endpoints that ASP.NET Core explicitly
/// marks anonymous. Microsoft.OpenApi omits an empty collection during
/// serialization, while <c>[{}]</c> is the OpenAPI representation for allowing
/// anonymous access and is retained in the runtime Swagger document.
/// </summary>
public sealed class AuthorizationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<IAllowAnonymous>()
            .Any())
        {
            operation.Security = [new OpenApiSecurityRequirement()];
        }
    }
}
