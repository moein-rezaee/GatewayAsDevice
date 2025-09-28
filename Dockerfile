# syntax=docker/dockerfile:1.7-labs
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY SepidarGateway.sln ./
COPY SepidarGateway.Api/SepidarGateway.Api.csproj SepidarGateway.Api/
RUN dotnet restore SepidarGateway.Api/SepidarGateway.Api.csproj

COPY . .
RUN dotnet publish SepidarGateway.Api/SepidarGateway.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:5090

COPY --from=build /app/publish ./
COPY ./.env ./.env

EXPOSE 5090

ENTRYPOINT ["dotnet", "SepidarGateway.Api.dll"]
