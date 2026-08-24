# Multi-stage Dockerfile for PocketGoal ASP.NET Core 8.0 MVC application

# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["PocketGoal/PocketGoal/PocketGoal.csproj", "PocketGoal/PocketGoal/"]
RUN dotnet restore "PocketGoal/PocketGoal/PocketGoal.csproj"

# Copy all source files and publish release build
COPY . .
WORKDIR "/src/PocketGoal/PocketGoal"
RUN dotnet publish "PocketGoal.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Expose port (Render sets $PORT dynamically, defaults to 8080)
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PocketGoal.dll"]
