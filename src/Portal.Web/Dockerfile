FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/Portal.Contracts/Portal.Contracts.csproj src/Portal.Contracts/
COPY src/Portal.Web/Portal.Web.csproj src/Portal.Web/
RUN dotnet restore src/Portal.Web/Portal.Web.csproj
COPY src/Portal.Contracts/ src/Portal.Contracts/
COPY src/Portal.Web/ src/Portal.Web/
RUN dotnet publish src/Portal.Web/Portal.Web.csproj -c Release --no-restore -o /app

FROM caddy:2-alpine AS final
COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /app/wwwroot /srv
EXPOSE 80 443
