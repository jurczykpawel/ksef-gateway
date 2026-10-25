FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:afd0ccff9d306812a69ef0f47458376b6acb544ae866cfc4b8d28ff0eef35ea7 AS build
WORKDIR /src

COPY vendor/nuget/ /vendor/nuget/
COPY src/nuget.config ./nuget.config
COPY src/KSeFGateway.Api/KSeFGateway.Api.csproj KSeFGateway.Api/
RUN dotnet restore KSeFGateway.Api/KSeFGateway.Api.csproj --configfile nuget.config

# Copy source and build
COPY src/KSeFGateway.Api/ KSeFGateway.Api/
RUN dotnet publish KSeFGateway.Api/KSeFGateway.Api.csproj \
    -c Release -o /app/publish --no-restore

# Test (run via: docker compose run --rm ksef-api-tests)
FROM build AS test
COPY . /repo/
COPY src/KSeFGateway.Api.Tests/ KSeFGateway.Api.Tests/
RUN dotnet restore KSeFGateway.Api.Tests/KSeFGateway.Api.Tests.csproj --configfile nuget.config
ENTRYPOINT ["dotnet", "test", "KSeFGateway.Api.Tests/KSeFGateway.Api.Tests.csproj", "-c", "Release", "--filter", "Category!=Integration"]

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca AS runtime
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
