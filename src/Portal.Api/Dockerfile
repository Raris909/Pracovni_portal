FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/Portal.Contracts/Portal.Contracts.csproj src/Portal.Contracts/
COPY src/Portal.Api/Portal.Api.csproj src/Portal.Api/
RUN dotnet restore src/Portal.Api/Portal.Api.csproj
COPY src/Portal.Contracts/ src/Portal.Contracts/
COPY src/Portal.Api/ src/Portal.Api/
RUN dotnet publish src/Portal.Api/Portal.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Portal.Api.dll"]
