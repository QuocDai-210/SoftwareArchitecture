FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
ARG SERVICE
WORKDIR /work
COPY Directory.Build.props global.json ./
COPY src ./src
RUN dotnet restore src/${SERVICE}/${SERVICE}.csproj --locked-mode
RUN dotnet publish src/${SERVICE}/${SERVICE}.csproj -c Release --no-restore -o /publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
ARG SERVICE
WORKDIR /app
COPY --from=build /publish .
ENV ASPNETCORE_HTTP_PORTS=8080 SERVICE_DLL=${SERVICE}.dll
USER $APP_UID
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet \"$SERVICE_DLL\""]
