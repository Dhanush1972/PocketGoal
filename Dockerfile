# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["PocketGoal/PocketGoal/PocketGoal.csproj", "PocketGoal/PocketGoal/"]
RUN dotnet restore "PocketGoal/PocketGoal/PocketGoal.csproj"

COPY . .
WORKDIR "/src/PocketGoal/PocketGoal"
RUN dotnet publish "PocketGoal.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
USER app
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet PocketGoal.dll"]
