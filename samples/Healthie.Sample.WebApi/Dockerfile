# Build from the repository root so the shared build configuration and the src/ projects the sample
# references are in context:
#   docker build -f samples/Healthie.Sample.WebApi/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the manifests alone first, so this layer is only rebuilt when dependencies change.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
COPY samples/Healthie.Sample.WebApi/Healthie.Sample.WebApi.csproj samples/Healthie.Sample.WebApi/
RUN dotnet restore samples/Healthie.Sample.WebApi/Healthie.Sample.WebApi.csproj

COPY samples/ samples/
RUN dotnet publish samples/Healthie.Sample.WebApi/Healthie.Sample.WebApi.csproj \
    --configuration Release --no-restore --output /app

# The samples target net8.0, so they run on the .NET 8 runtime even though the .NET 10 SDK builds
# them: the libraries they reference target both, and the newer SDK is needed to build net10.0.
# This also exercises the library from a .NET 8 consumer.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Run as the non-root user the base image provides.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Healthie.Sample.WebApi.dll"]
