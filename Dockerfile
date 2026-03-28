# ── Stage 1: Build ───────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution / project files first for layer-cache efficiency
COPY PIYA_API/PIYA_API.csproj PIYA_API/
RUN dotnet restore PIYA_API/PIYA_API.csproj

# Copy the rest of the source
COPY PIYA_API/ PIYA_API/
WORKDIR /src/PIYA_API
RUN dotnet publish -c Release -o /app/publish --no-restore

# ── Stage 2: Runtime ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN addgroup --system piya && adduser --system --ingroup piya piya

# Create directories the app writes to at runtime
RUN mkdir -p /app/logs /app/uploads && chown -R piya:piya /app

COPY --from=build /app/publish .

USER piya

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "PIYA_API.dll"]
