# Dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy projects that MessageBus.Invalidator depends on
COPY SharedLibrary/SharedLibrary.csproj SharedLibrary/
COPY MessageBus.Invalidator/MessageBus.Invalidator.csproj MessageBus.Invalidator/

# Restore worker project
RUN dotnet restore MessageBus.Invalidator/MessageBus.Invalidator.csproj

# Copy everything and publish
COPY . .
RUN dotnet publish MessageBus.Invalidator/MessageBus.Invalidator.csproj -c Release -o /app

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

COPY --from=build /app .

ENTRYPOINT ["dotnet", "MessageBus.Invalidator.dll"]