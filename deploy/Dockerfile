FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG PROJECT_PATH
ARG PROJECT_NAME
WORKDIR /src
COPY ["${PROJECT_PATH}/${PROJECT_NAME}.csproj", "${PROJECT_PATH}/"]
RUN dotnet restore "${PROJECT_PATH}/${PROJECT_NAME}.csproj"
COPY . .
WORKDIR "/src/${PROJECT_PATH}"
RUN dotnet build "${PROJECT_NAME}.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG PROJECT_NAME
RUN dotnet publish "${PROJECT_NAME}.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
ARG PROJECT_NAME
WORKDIR /app
COPY --from=publish /app/publish .
RUN echo "#!/bin/sh" > /entrypoint.sh && \
    echo "exec dotnet /app/${PROJECT_NAME}.dll \"\$@\"" >> /entrypoint.sh && \
    chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
