FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["global.json", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["src/ProcessingWorker/ProcessingWorker.csproj", "src/ProcessingWorker/"]
RUN dotnet restore "src/ProcessingWorker/ProcessingWorker.csproj"

COPY ["src/ProcessingWorker/", "src/ProcessingWorker/"]
COPY ["src/Shared/", "src/Shared/"]
WORKDIR /src/src/ProcessingWorker
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ProcessingWorker.dll"]