FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b
WORKDIR /repo/tools/TokenGenerator
COPY vendor/nuget/ /repo/vendor/nuget/
COPY tools/TokenGenerator/nuget.config ./nuget.config
COPY tools/TokenGenerator/TokenGenerator.csproj .
RUN dotnet restore --configfile nuget.config

COPY tools/TokenGenerator/Program.cs .
RUN dotnet build --no-restore

ENTRYPOINT ["dotnet", "run"]
