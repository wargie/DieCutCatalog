FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY DieCutCatalog.sln ./
COPY NuGet.Config ./
COPY src/DieCutCatalog.Api/DieCutCatalog.Api.csproj src/DieCutCatalog.Api/
COPY src/DieCutCatalog.Application/DieCutCatalog.Application.csproj src/DieCutCatalog.Application/
COPY src/DieCutCatalog.Domain/DieCutCatalog.Domain.csproj src/DieCutCatalog.Domain/
COPY src/DieCutCatalog.Infrastructure/DieCutCatalog.Infrastructure.csproj src/DieCutCatalog.Infrastructure/
RUN dotnet restore src/DieCutCatalog.Api/DieCutCatalog.Api.csproj --configfile NuGet.Config

COPY src/ src/
RUN dotnet publish src/DieCutCatalog.Api/DieCutCatalog.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
RUN apk add --no-cache font-dejavu \
    && addgroup -S diecut \
    && adduser -S diecut -G diecut \
    && mkdir -p /var/lib/diecut-catalog/storage \
    && chown -R diecut:diecut /var/lib/diecut-catalog
ENV DIECUT_FONT_PATH=/usr/share/fonts/dejavu/DejaVuSans.ttf
ENV DIECUT_BOLD_FONT_PATH=/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf
COPY --from=build --chown=diecut:diecut /app/publish ./
USER diecut
EXPOSE 8080
ENTRYPOINT ["dotnet", "DieCutCatalog.Api.dll"]
