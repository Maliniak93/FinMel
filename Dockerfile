FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /WebApi
COPY . .
RUN dotnet restore
RUN dotnet build -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /WebApi
COPY --from=build /WebApi/out .

ENV ASPNETCORE_URLS http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Development
EXPOSE 5000/tcp

ENTRYPOINT ["dotnet", "WebApi.dll"]
