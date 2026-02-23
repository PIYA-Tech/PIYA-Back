using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PIYA_API.Swagger;

/// <summary>
/// Operation filter to correctly document multipart/form-data endpoints with IFormFile parameters
/// </summary>
public class FileUploadOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var parameters = context.MethodInfo.GetParameters();
        var hasFile = parameters.Any(p => p.ParameterType == typeof(IFormFile) || p.ParameterType == typeof(IFormFileCollection));
        if (!hasFile) return;

        operation.RequestBody = new OpenApiRequestBody
        {
            Content =
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, OpenApiSchema>()
                    }
                }
            }
        };

        // Add file and other form parameters
        foreach (var param in parameters)
        {
            var name = param.Name ?? "file";
            if (param.ParameterType == typeof(IFormFile) || param.ParameterType == typeof(IFormFileCollection))
            {
                operation.RequestBody.Content["multipart/form-data"].Schema.Properties[name] = new OpenApiSchema
                {
                    Type = "string",
                    Format = "binary"
                };
            }
            else if (param.GetCustomAttributes(true).Any(a => a is Microsoft.AspNetCore.Mvc.FromFormAttribute))
            {
                operation.RequestBody.Content["multipart/form-data"].Schema.Properties[name] = new OpenApiSchema
                {
                    Type = "string"
                };
            }
        }
    }
}
