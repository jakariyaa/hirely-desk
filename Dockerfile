FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY . .

RUN dotnet restore src/CvPlatform.Web/CvPlatform.Web.csproj

RUN dotnet publish src/CvPlatform.Web/CvPlatform.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

RUN mkdir -p /app/logs /home/app/.aspnet/DataProtection-Keys \
    && chown -R 1654:1654 /app /home/app/.aspnet

USER 1654

ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "CvPlatform.Web.dll"]
