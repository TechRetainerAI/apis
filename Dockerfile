FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY MeDan.Api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
# Cloud hosts (Render, Cloud Run) inject PORT; default to 8080.
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080}
# Shared hosts cap inotify instances; config hot-reload isn't needed in prod.
ENV DOTNET_hostBuilder__reloadConfigOnChange=false
EXPOSE 8080
ENTRYPOINT ["dotnet", "MeDan.Api.dll"]
