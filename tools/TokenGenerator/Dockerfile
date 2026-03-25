FROM mcr.microsoft.com/dotnet/sdk:9.0
WORKDIR /app

ARG GITHUB_PAT
ENV GITHUB_PAT=${GITHUB_PAT}

COPY nuget.config ./nuget.config
COPY TokenGenerator.csproj .
RUN dotnet restore --configfile nuget.config

COPY Program.cs .
ENTRYPOINT ["dotnet", "run"]
