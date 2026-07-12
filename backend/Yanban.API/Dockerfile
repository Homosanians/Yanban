FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Copy csproj files first for restore-layer caching (build context = ./backend)
COPY ["Yanban.API/Yanban.API.csproj", "Yanban.API/"]
COPY ["Yanban.Application/Yanban.Application.csproj", "Yanban.Application/"]
COPY ["Yanban.Infrastructure/Yanban.Infrastructure.csproj", "Yanban.Infrastructure/"]
COPY ["Yanban.Domain/Yanban.Domain.csproj", "Yanban.Domain/"]
RUN dotnet restore "Yanban.API/Yanban.API.csproj"
COPY . .
WORKDIR "/src/Yanban.API"
RUN dotnet publish "./Yanban.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Yanban.API.dll"]
