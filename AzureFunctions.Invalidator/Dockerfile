# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

# Copy project files
COPY UserManagement.Contracts/UserManagement.Contracts.csproj UserManagement.Contracts/
COPY SharedLibrary/SharedLibrary.csproj SharedLibrary/
COPY AzureFunctions.Invalidator/AzureFunctions.Invalidator.csproj AzureFunctions.Invalidator/

# Restore dependencies
RUN dotnet restore AzureFunctions.Invalidator/AzureFunctions.Invalidator.csproj

# Copy source code
COPY UserManagement.Contracts/ UserManagement.Contracts/
COPY SharedLibrary/ SharedLibrary/
COPY AzureFunctions.Invalidator/ AzureFunctions.Invalidator/

# Publish Azure Function
RUN dotnet publish AzureFunctions.Invalidator/AzureFunctions.Invalidator.csproj \
    -c Release \
    -o /app/publish \
    --force

# Stage 2: Azure Functions runtime
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated9.0

ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated

COPY --from=build /app/publish /home/site/wwwroot