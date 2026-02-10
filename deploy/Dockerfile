FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

# Build on the native host platform (amd64) and cross-compile for the target (arm64)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
ARG BUILD_CONFIGURATION=Release
ARG PROJECT_PATH
ARG PROJECT_NAME
WORKDIR /src
# Copy entire source tree first to ensure all dependencies can be resolved
COPY . .
# Restore dependencies with the full context
RUN dotnet restore "${PROJECT_PATH}/${PROJECT_NAME}.csproj" -a ${TARGETARCH/amd64/x64}
WORKDIR "/src/${PROJECT_PATH}"
RUN dotnet build "${PROJECT_NAME}.csproj" -c $BUILD_CONFIGURATION -o /app/build -a ${TARGETARCH/amd64/x64}

FROM build AS publish
ARG TARGETARCH
ARG BUILD_CONFIGURATION=Release
ARG PROJECT_NAME
RUN dotnet publish "${PROJECT_NAME}.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false -a ${TARGETARCH/amd64/x64}

FROM base AS final
ARG PROJECT_NAME
WORKDIR /app
COPY --from=publish /app/publish .
USER root
RUN echo "#!/bin/sh" > /app/entrypoint.sh && \
    echo "exec dotnet /app/${PROJECT_NAME}.dll \"\$@\"" >> /app/entrypoint.sh && \
    chmod +x /app/entrypoint.sh
USER $APP_UID
ENTRYPOINT ["/app/entrypoint.sh"]
