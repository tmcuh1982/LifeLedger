FROM node:22-alpine AS web-build
WORKDIR /app
COPY src/lifeledger-web ./src/lifeledger-web
WORKDIR /app/src/lifeledger-web
RUN npm install
RUN mkdir -p ../LifeLedger.Api && npm run build

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS api-build
WORKDIR /src
COPY LifeLedger.sln ./
COPY src/LifeLedger.Api/LifeLedger.Api.csproj ./src/LifeLedger.Api/
RUN dotnet restore LifeLedger.sln
COPY src/LifeLedger.Api ./src/LifeLedger.Api
COPY --from=web-build /app/src/LifeLedger.Api/wwwroot ./src/LifeLedger.Api/wwwroot
RUN dotnet publish src/LifeLedger.Api/LifeLedger.Api.csproj --no-restore -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=api-build /app/publish .
VOLUME ["/app/data"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "LifeLedger.Api.dll"]
