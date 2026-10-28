FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
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
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee AS runtime
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
