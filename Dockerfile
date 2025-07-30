FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/FinMel.Web/FinMel.Web.csproj", "src/FinMel.Web/"]
COPY ["src/FinMel.Application/FinMel.Application.csproj", "src/FinMel.Application/"]
COPY ["src/FinMel.Domain/FinMel.Domain.csproj", "src/FinMel.Domain/"]
COPY ["src/FinMel.Contracts/FinMel.Contracts.csproj", "src/FinMel.Contracts/"]
COPY ["src/FinMel.Infrastructure/FinMel.Infrastructure.csproj", "src/FinMel.Infrastructure/"]
COPY ["src/FinMel.ServiceDefaults/FinMel.ServiceDefaults.csproj", "src/FinMel.ServiceDefaults/"]
RUN dotnet restore "./src/FinMel.Web/FinMel.Web.csproj"
COPY . .
WORKDIR "/src/src/FinMel.Web"
RUN dotnet build "./FinMel.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./FinMel.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FinMel.Web.dll"]
