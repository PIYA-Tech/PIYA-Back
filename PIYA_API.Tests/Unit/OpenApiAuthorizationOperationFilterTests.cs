using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi.Models;
using Moq;
using PIYA_API.Controllers;
using PIYA_API.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace PIYA_API.Tests.Unit;

public class OpenApiAuthorizationOperationFilterTests
{
    [Fact]
    public void AllowAnonymousOperation_OverridesDocumentBearerRequirement()
    {
        var operation = new OpenApiOperation
        {
            Security = [new OpenApiSecurityRequirement()]
        };
        var apiDescription = new ApiDescription
        {
            ActionDescriptor = new ActionDescriptor
            {
                EndpointMetadata = [new AllowAnonymousAttribute()]
            }
        };
        var context = new OperationFilterContext(
            apiDescription,
            Mock.Of<ISchemaGenerator>(),
            new SchemaRepository(),
            typeof(AuthController).GetMethod(nameof(AuthController.Login))!);

        new AuthorizationOperationFilter().Apply(operation, context);

        operation.Security.Should().ContainSingle()
            .Which.Should().BeEmpty(
                "an empty security requirement permits anonymous access");
    }
}
