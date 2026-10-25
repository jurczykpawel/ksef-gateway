FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2
WORKDIR /repo/tools/CertGenerator
COPY vendor/nuget/ /repo/vendor/nuget/
COPY tools/CertGenerator/nuget.config ./nuget.config
COPY tools/CertGenerator/CertGenerator.csproj .
RUN dotnet restore --configfile nuget.config

COPY tools/CertGenerator/Program.cs .

RUN dotnet build --no-restore

RUN mkdir -p /app/output

ENTRYPOINT ["dotnet", "run"]
