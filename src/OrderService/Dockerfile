FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["global.json", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["src/OrderService/OrderService.csproj", "src/OrderService/"]
RUN dotnet restore "src/OrderService/OrderService.csproj"

COPY ["src/OrderService/", "src/OrderService/"]
COPY ["src/Shared/", "src/Shared/"]
WORKDIR /src/src/OrderService
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OrderService.dll"]