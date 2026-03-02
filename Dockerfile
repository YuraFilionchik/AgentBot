# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
ARG APP_UID=1000
ARG APP_GID=1000
# create group/user with given IDs and prepare app directory
RUN groupadd --gid $APP_GID appgroup \
    && useradd --uid $APP_UID --gid $APP_GID --create-home --home-dir /app appuser \
    && mkdir -p /app \
    && chown appuser:appgroup /app
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["AgentBot.csproj", "."]
RUN dotnet restore "./AgentBot.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./AgentBot.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AgentBot.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
# switch to root to be able to copy files and change ownership
USER root
WORKDIR /app
COPY --from=publish /app/publish .
# ensure files are owned by the non-root user
RUN chown -R appuser:appgroup /app
# switch to non-root user for running the app
USER appuser

# Load environment variables from .env file (if exists)
# Variables can also be passed at runtime via docker run -e or docker-compose
ENTRYPOINT ["dotnet", "AgentBot.dll"]