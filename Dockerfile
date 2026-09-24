FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY src/CvPlatform.Core/CvPlatform.Core.csproj src/CvPlatform.Core/
COPY src/CvPlatform.Application/CvPlatform.Application.csproj src/CvPlatform.Application/
COPY src/CvPlatform.Infrastructure/CvPlatform.Infrastructure.csproj src/CvPlatform.Infrastructure/
COPY src/CvPlatform.Web/CvPlatform.Web.csproj src/CvPlatform.Web/

RUN dotnet restore src/CvPlatform.Web/CvPlatform.Web.csproj

COPY . .

RUN dotnet publish src/CvPlatform.Web/CvPlatform.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

RUN mkdir -p /app/logs && chown -R 1654:1654 /app

USER 1654

ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "CvPlatform.Web.dll"]
