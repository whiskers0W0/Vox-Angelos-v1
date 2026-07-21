# Stage 1: Build the app using .NET 10 SDK
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Copy the project file specifically for the restore step
COPY ["VoxAngelos/VoxAngelos.csproj", "VoxAngelos/"]
RUN dotnet restore "VoxAngelos/VoxAngelos.csproj"
# Copy everything else and build
COPY . .
WORKDIR "/src/VoxAngelos"
RUN dotnet build "VoxAngelos.csproj" -c Release -o /app/build

# Stage 2: Publish the app
FROM build AS publish
RUN dotnet publish "VoxAngelos.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Final Stage - MUST match the SDK version (10.0)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Npgsql needs this to negotiate connections with PostgreSQL; not guaranteed present in the base image
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=publish /app/publish .

# Render configuration
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "VoxAngelos.dll"]
