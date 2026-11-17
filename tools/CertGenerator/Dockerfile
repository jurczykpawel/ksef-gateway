FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb
WORKDIR /repo/tools/CertGenerator
COPY vendor/nuget/ /repo/vendor/nuget/
COPY tools/CertGenerator/nuget.config ./nuget.config
COPY tools/CertGenerator/CertGenerator.csproj .
RUN dotnet restore --configfile nuget.config

COPY tools/CertGenerator/Program.cs .

RUN dotnet build --no-restore

RUN mkdir -p /app/output

ENTRYPOINT ["dotnet", "run"]
