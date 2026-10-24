FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2
WORKDIR /app

# GitHub PAT (read:packages) for the CIRFMF NuGet feed on GitHub Packages.
# Preferred: BuildKit secret (never recorded in image history or provenance attestations):
#   docker build --secret id=GITHUB_PAT,env=GITHUB_PAT ...
# Fallback for builders without secret support (e.g. Render): --build-arg GITHUB_PAT=...
# Deliberately NOT promoted to ENV, so it never ends up in the image config.
ARG GITHUB_PAT

COPY nuget.config ./nuget.config
COPY CertGenerator.csproj .
RUN --mount=type=secret,id=GITHUB_PAT,required=false \
    GITHUB_PAT="$(cat /run/secrets/GITHUB_PAT 2>/dev/null || printf '%s' "$GITHUB_PAT")" \
    dotnet restore --configfile nuget.config

COPY Program.cs .

RUN mkdir -p /app/output

ENTRYPOINT ["dotnet", "run"]
