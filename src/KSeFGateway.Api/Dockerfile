FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Accept GITHUB_PAT as build arg for CIRFMF NuGet packages
ARG GITHUB_PAT
ENV GITHUB_PAT=${GITHUB_PAT}

# Copy nuget.config to workdir root (dotnet looks upward from project)
COPY KSeFGateway.Api/nuget.config ./nuget.config
COPY KSeFGateway.Api/KSeFGateway.Api.csproj KSeFGateway.Api/
RUN dotnet restore KSeFGateway.Api/KSeFGateway.Api.csproj --configfile nuget.config

# Copy source and build
COPY KSeFGateway.Api/ KSeFGateway.Api/
RUN dotnet publish KSeFGateway.Api/KSeFGateway.Api.csproj \
    -c Release -o /app/publish --no-restore

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "KSeFGateway.Api.dll"]
