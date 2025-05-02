FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["junie-des-1942stats/junie-des-1942stats.csproj", "junie-des-1942stats/"]
RUN dotnet restore "junie-des-1942stats/junie-des-1942stats.csproj"
COPY . .
WORKDIR "/src/junie-des-1942stats"
RUN dotnet build "./junie-des-1942stats.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./junie-des-1942stats.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "junie-des-1942stats.dll"]
