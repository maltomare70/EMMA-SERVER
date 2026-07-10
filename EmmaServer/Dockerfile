FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# 1. Copia il file di progetto per sfruttare la cache di Docker sul restore delle dipendenze
COPY ["EmmaServer/EmmaServer.csproj", "EmmaServer/"]
RUN dotnet restore "EmmaServer/EmmaServer.csproj"

# 2. Copia l'intero codice sorgente
COPY . .

# 3. Esegue la build rimanendo nella cartella radice (/src) per evitare problemi con i percorsi relativi
RUN dotnet build "EmmaServer/EmmaServer.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "EmmaServer/EmmaServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EmmaServer.dll"]