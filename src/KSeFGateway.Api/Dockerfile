FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# GitHub PAT (read:packages) for the CIRFMF NuGet feed on GitHub Packages.
# Preferred: BuildKit secret (never recorded in image history or provenance attestations):
#   docker build --secret id=GITHUB_PAT,env=GITHUB_PAT ...
# Fallback for builders without secret support (e.g. Render): --build-arg GITHUB_PAT=...
# Deliberately NOT promoted to ENV, so it never ends up in the image config.
ARG GITHUB_PAT

# Copy nuget.config to workdir root (dotnet looks upward from project)
COPY KSeFGateway.Api/nuget.config ./nuget.config
COPY KSeFGateway.Api/KSeFGateway.Api.csproj KSeFGateway.Api/
RUN --mount=type=secret,id=GITHUB_PAT,required=false \
    GITHUB_PAT="$(cat /run/secrets/GITHUB_PAT 2>/dev/null || printf '%s' "$GITHUB_PAT")" \
    dotnet restore KSeFGateway.Api/KSeFGateway.Api.csproj --configfile nuget.config

# Copy source and build
COPY KSeFGateway.Api/ KSeFGateway.Api/
RUN dotnet publish KSeFGateway.Api/KSeFGateway.Api.csproj \
    -c Release -o /app/publish --no-restore

# Test (run via: docker compose run --rm ksef-api-tests)
FROM build AS test
COPY KSeFGateway.Api.Tests/ KSeFGateway.Api.Tests/
RUN --mount=type=secret,id=GITHUB_PAT,required=false \
    GITHUB_PAT="$(cat /run/secrets/GITHUB_PAT 2>/dev/null || printf '%s' "$GITHUB_PAT")" \
    dotnet restore KSeFGateway.Api.Tests/KSeFGateway.Api.Tests.csproj --configfile nuget.config
ENTRYPOINT ["dotnet", "test", "KSeFGateway.Api.Tests/KSeFGateway.Api.Tests.csproj", "-c", "Release", "--filter", "Category!=Integration"]

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Render runtime Secret Files are readable by group 1000.
RUN (getent group 1000 >/dev/null || groupadd --gid 1000 render-secrets) \
    && usermod -a -G 1000 app

COPY --from=build /app/publish .
COPY LICENSE THIRD_PARTY_NOTICES.md /app/licenses/

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app

ENTRYPOINT ["dotnet", "KSeFGateway.Api.dll"]
