# syntax=docker/dockerfile:1
# Build context: repository root (contracts/ and Skarbiec.ServiceDefaults/ are project references
# of Skarbiec.ServiceDefaults, which the Gateway itself references).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY contracts/Skarbiec.Contracts/Skarbiec.Contracts.csproj contracts/Skarbiec.Contracts/
COPY Skarbiec.ServiceDefaults/Skarbiec.ServiceDefaults.csproj Skarbiec.ServiceDefaults/
COPY gateway/Skarbiec.Gateway/Skarbiec.Gateway.csproj gateway/Skarbiec.Gateway/
RUN dotnet restore gateway/Skarbiec.Gateway/Skarbiec.Gateway.csproj

COPY contracts/Skarbiec.Contracts/ contracts/Skarbiec.Contracts/
COPY Skarbiec.ServiceDefaults/ Skarbiec.ServiceDefaults/
COPY gateway/Skarbiec.Gateway/ gateway/Skarbiec.Gateway/
RUN dotnet publish gateway/Skarbiec.Gateway/Skarbiec.Gateway.csproj \
    --configuration Release \
    --no-restore \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN useradd --uid 5678 --user-group --no-create-home --shell /usr/sbin/nologin appuser \
    && chown -R appuser /app
COPY --from=build --chown=appuser:appuser /app .
USER appuser
EXPOSE 8080
ENTRYPOINT ["dotnet", "Skarbiec.Gateway.dll"]
