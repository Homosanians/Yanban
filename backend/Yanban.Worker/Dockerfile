# Build context is ./backend (same as the API's), so every path here is relative to that.
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# csproj files first, so a source-only change does not invalidate the restore layer.
COPY ["Yanban.Worker/Yanban.Worker.csproj", "Yanban.Worker/"]
COPY ["Yanban.Application/Yanban.Application.csproj", "Yanban.Application/"]
COPY ["Yanban.Infrastructure/Yanban.Infrastructure.csproj", "Yanban.Infrastructure/"]
COPY ["Yanban.Domain/Yanban.Domain.csproj", "Yanban.Domain/"]
RUN dotnet restore "Yanban.Worker/Yanban.Worker.csproj"
COPY . .
WORKDIR "/src/Yanban.Worker"
RUN dotnet publish "./Yanban.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
# No ASPNETCORE_URLS and no EXPOSE: this process listens on nothing. It only reaches out.
ENTRYPOINT ["dotnet", "Yanban.Worker.dll"]
