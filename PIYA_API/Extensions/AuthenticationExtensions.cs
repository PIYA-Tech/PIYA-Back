using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PIYA_API.Service.Interface;
using Serilog;

namespace PIYA_API.Extensions;

/// <summary>
/// JWT authentication and role-based authorization configuration.
/// </summary>
public static class AuthenticationExtensions
{
    public static IServiceCollection AddPiyaAuthentication(
        this IServiceCollection services, IConfiguration config)
    {
        var jwtSecretKey = config["Jwt:SecretKey"]
            ?? throw new InvalidOperationException(
                "Jwt:SecretKey is not configured. Set the Jwt__SecretKey environment variable. " +
                "Generate with: openssl rand -base64 64");

        if (jwtSecretKey.Length < 32)
            throw new InvalidOperationException("Jwt:SecretKey must be at least 32 characters.");

        if (jwtSecretKey.Contains("REPLACE") || jwtSecretKey.Contains("CHANGE"))
            throw new InvalidOperationException(
                "Jwt:SecretKey must be changed from placeholder value. " +
                "Generate with: openssl rand -base64 64");

        var jwtIssuer = config["Jwt:Issuer"] ?? "PIYA_API";
        var jwtAudience = config["Jwt:Audience"] ?? "PIYA_Clients";

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
                ValidateIssuer = true,
                ValidIssuer = jwtIssuer,
                ValidateAudience = true,
                ValidAudience = jwtAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var path = context.HttpContext.Request.Path;
                    // Browsers cannot attach an Authorization header to the
                    // WebSocket/SSE handshake. Never accept query tokens on APIs.
                    if (string.IsNullOrEmpty(context.Request.Headers.Authorization) &&
                        (path.StartsWithSegments("/notificationHub") ||
                         path.StartsWithSegments("/hubs/pharmacy") ||
                         path.StartsWithSegments("/hubs/inventory")) &&
                        context.Request.Query.TryGetValue("access_token", out var token) && token.Count == 1)
                        context.Token = token[0];
                    return Task.CompletedTask;
                },
                OnAuthenticationFailed = context =>
                {
                    Log.Debug("JWT Auth Failed: {Message}", context.Exception.Message);
                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    var jwtSvc = context.HttpContext.RequestServices.GetRequiredService<IJwtService>();
                    if (context.Principal is null || !await jwtSvc.IsSessionCurrentAsync(context.Principal))
                    {
                        context.Fail("This session has ended. Sign in again.");
                        return;
                    }
                    var jti = context.Principal?.FindFirst(
                        System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
                    if (!string.IsNullOrWhiteSpace(jti))
                    {
                        var revoked = await jwtSvc.IsJtiRevokedAsync(jti);
                        if (revoked)
                        {
                            context.Fail("Token has been revoked");
                        }
                    }
                }
            };
        });

        return services;
    }

    public static IServiceCollection AddPiyaAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy("PatientOnly", policy => policy.RequireRole("Patient"))
            .AddPolicy("DoctorOnly", policy => policy.RequireRole("Doctor"))
            .AddPolicy("PharmacistOnly", policy => policy.RequireRole("Pharmacist"))
            .AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"))
            .AddPolicy("DoctorOrAdmin", policy => policy.RequireRole("Doctor", "Admin"))
            .AddPolicy("PharmacistOrAdmin", policy => policy.RequireRole("Pharmacist", "Admin"))
            .AddPolicy("HealthcareProfessional", policy =>
                policy.RequireRole("Doctor", "Pharmacist", "Admin"));

        return services;
    }
}
