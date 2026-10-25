FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:afd0ccff9d306812a69ef0f47458376b6acb544ae866cfc4b8d28ff0eef35ea7
WORKDIR /repo/tools/CertGenerator
COPY vendor/nuget/ /repo/vendor/nuget/
COPY tools/CertGenerator/nuget.config ./nuget.config
COPY tools/CertGenerator/CertGenerator.csproj .
RUN dotnet restore --configfile nuget.config

COPY tools/CertGenerator/Program.cs .

RUN dotnet build --no-restore

RUN mkdir -p /app/output

ENTRYPOINT ["dotnet", "run"]
