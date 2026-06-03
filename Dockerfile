# syntax=docker/dockerfile:1.7
# Multi-stage build for ScanGo.Api (.NET 10)

ARG DOTNET_VERSION=10.0

# ----- Build stage -----
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# Restore (cached when *.csproj unchanged)
COPY ScanGo.Api/ScanGo.Api.csproj ScanGo.Api/
RUN dotnet restore ScanGo.Api/ScanGo.Api.csproj

# Build + publish
COPY ScanGo.Api/ ScanGo.Api/
RUN dotnet publish ScanGo.Api/ScanGo.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ----- Runtime stage -----
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# Npgsql probes libgssapi (Kerberos) when opening a connection; the slim aspnet
# image doesn't ship it, which spams "Cannot load library libgssapi_krb5.so.2".
# We auth with a password so it's harmless — install the lib to silence it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

ENTRYPOINT ["dotnet", "ScanGo.Api.dll"]
