FROM mcr.microsoft.com/dotnet/sdk:11.0@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea
WORKDIR /repo/tools/CertGenerator
COPY vendor/nuget/ /repo/vendor/nuget/
COPY tools/CertGenerator/nuget.config ./nuget.config
COPY tools/CertGenerator/CertGenerator.csproj .
RUN dotnet restore --configfile nuget.config

COPY tools/CertGenerator/Program.cs .

RUN dotnet build --no-restore

RUN mkdir -p /app/output

ENTRYPOINT ["dotnet", "run"]
