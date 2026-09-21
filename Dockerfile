# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1. Copy project descriptors and solution for optimized layer caching
COPY ["Directory.Build.props", "global.json", "RapidRelief.sln", "./"]
COPY ["src/RapidRelief.Shared/RapidRelief.Shared.csproj", "src/RapidRelief.Shared/"]
COPY ["src/RapidRelief.Client/RapidRelief.Client.csproj", "src/RapidRelief.Client/"]
COPY ["src/RapidRelief.Api/RapidRelief.Api.csproj", "src/RapidRelief.Api/"]
COPY ["tests/RapidRelief.Api.Tests/RapidRelief.Api.Tests.csproj", "tests/RapidRelief.Api.Tests/"]
COPY ["tests/RapidRelief.Architecture.Tests/RapidRelief.Architecture.Tests.csproj", "tests/RapidRelief.Architecture.Tests/"]

# Restore dependencies for the API (which pulls Shared & Client transitively)
RUN dotnet restore "src/RapidRelief.Api/RapidRelief.Api.csproj"

# 2. Copy the entire source and build Release package
COPY . .
WORKDIR "/src/src/RapidRelief.Api"
RUN dotnet publish "RapidRelief.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 3. Final lightweight runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

# Install ICU globalization libraries and timezone data for alpine
RUN apk add --no-cache icu-libs tzdata
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Create writable upload directory for emergency photos
RUN mkdir -p /app/App_Data/uploads

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "RapidRelief.Api.dll"]
