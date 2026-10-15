# Build context must be the repository root, e.g. `docker build -f .eng/docker/Dockerfile --target api .`.
# One file, two deployable images: `--target api` and `--target worker`.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG VERSION=0.0.0-dev
WORKDIR /src

COPY DocRag.Ingestion.slnx Directory.Build.props stylecop.json ./
COPY src/DocRag.Ingestion.Domain/DocRag.Ingestion.Domain.csproj src/DocRag.Ingestion.Domain/
COPY src/DocRag.Ingestion.Application/DocRag.Ingestion.Application.csproj src/DocRag.Ingestion.Application/
COPY src/DocRag.Ingestion.Infrastructure/DocRag.Ingestion.Infrastructure.csproj src/DocRag.Ingestion.Infrastructure/
COPY src/DocRag.Ingestion.Api/DocRag.Ingestion.Api.csproj src/DocRag.Ingestion.Api/
COPY src/DocRag.Ingestion.Worker/DocRag.Ingestion.Worker.csproj src/DocRag.Ingestion.Worker/
RUN dotnet restore src/DocRag.Ingestion.Api/DocRag.Ingestion.Api.csproj \
 && dotnet restore src/DocRag.Ingestion.Worker/DocRag.Ingestion.Worker.csproj

COPY src/ src/
RUN dotnet publish src/DocRag.Ingestion.Api/DocRag.Ingestion.Api.csproj -c Release -o /out/api --no-restore -p:Version="$VERSION" \
 && dotnet publish src/DocRag.Ingestion.Worker/DocRag.Ingestion.Worker.csproj -c Release -o /out/worker --no-restore -p:Version="$VERSION"

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-base
WORKDIR /app
RUN useradd --create-home appuser
USER appuser
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

FROM runtime-base AS api
COPY --from=build --chown=appuser:appuser /out/api .
ENTRYPOINT ["dotnet", "DocRag.Ingestion.Api.dll"]

FROM runtime-base AS worker
COPY --from=build --chown=appuser:appuser /out/worker .
ENTRYPOINT ["dotnet", "DocRag.Ingestion.Worker.dll"]
