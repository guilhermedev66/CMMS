FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY ["Cmms.sln", "."]
COPY ["src/Cmms.Api/Cmms.Api.csproj", "src/Cmms.Api/"]
COPY ["src/Cmms.BuildingBlocks/Cmms.BuildingBlocks.csproj", "src/Cmms.BuildingBlocks/"]
COPY ["src/Modules/IdentityAccess/IdentityAccess.csproj", "src/Modules/IdentityAccess/"]
COPY ["src/Modules/Assets/Assets.csproj", "src/Modules/Assets/"]
RUN dotnet restore "src/Cmms.Api/Cmms.Api.csproj"

COPY . .
RUN dotnet publish "src/Cmms.Api/Cmms.Api.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && apt-get clean \
    && mkdir -p /home/app/.aspnet/DataProtection-Keys \
    && chown -R app:app /home/app/.aspnet
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Cmms.Api.dll"]
