FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["global.json", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["src/NotificationWorker/NotificationWorker.csproj", "src/NotificationWorker/"]
RUN dotnet restore "src/NotificationWorker/NotificationWorker.csproj"

COPY ["src/NotificationWorker/", "src/NotificationWorker/"]
COPY ["src/Shared/", "src/Shared/"]
WORKDIR /src/src/NotificationWorker
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "NotificationWorker.dll"]