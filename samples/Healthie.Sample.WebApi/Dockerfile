# Build from the repository root so the shared build configuration and the src/ projects the sample
# references are in context:
#   docker build -f samples/Healthie.Sample.WebApi/Dockerfile .
# Pinned by digest, not by tag. A tag is mutable, so "sdk:10.0" is a different image next
# week and a build that passed review is not the build that ships. Dependabot updates the
# digest the same way it updates a package version.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ed034a8bf0b24ded0cbbac07e17825d8e9ebfe21e308191d0f7421eaf5ad4664 AS build
WORKDIR /src

# Restore against the manifests alone first, so this layer is only rebuilt when dependencies change.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
COPY samples/Healthie.Sample.WebApi/Healthie.Sample.WebApi.csproj samples/Healthie.Sample.WebApi/packages.lock.json samples/Healthie.Sample.WebApi/
RUN dotnet restore samples/Healthie.Sample.WebApi/Healthie.Sample.WebApi.csproj --locked-mode

COPY samples/ samples/
RUN dotnet publish samples/Healthie.Sample.WebApi/Healthie.Sample.WebApi.csproj \
    --configuration Release --no-restore --output /app

# The samples target net8.0, so they run on the .NET 8 runtime even though the .NET 10 SDK builds
# them: the libraries they reference target both, and the newer SDK is needed to build net10.0.
# This also exercises the library from a .NET 8 consumer.
FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:8f6a307ae32fb393a4b4bcde0a81f0ce3f0a715de0e5575df71f3030448f2dde AS runtime
WORKDIR /app
COPY --from=build /app ./

# Run as the non-root user the base image provides.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Healthie.Sample.WebApi.dll"]
