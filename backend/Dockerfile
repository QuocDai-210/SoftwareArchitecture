FROM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
ARG SERVICE
WORKDIR /work
COPY Directory.Build.props global.json ./
COPY src ./src
RUN if [ "$SERVICE" = "ApiGateway" ]; then project="src/ApiGateway/ApiGateway.csproj"; else project="src/${SERVICE}/Api/${SERVICE}.Api.csproj"; fi; dotnet restore "$project" --locked-mode
RUN if [ "$SERVICE" = "ApiGateway" ]; then project="src/ApiGateway/ApiGateway.csproj"; else project="src/${SERVICE}/Api/${SERVICE}.Api.csproj"; fi; dotnet publish "$project" -c Release --no-restore -m:1 --disable-build-servers -p:UseSharedCompilation=false -o /publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0.14
ARG SERVICE
WORKDIR /app
COPY --from=build /publish .
ENV ASPNETCORE_HTTP_PORTS=8080 SERVICE_DLL=${SERVICE}.dll
USER $APP_UID
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet \"$SERVICE_DLL\""]
