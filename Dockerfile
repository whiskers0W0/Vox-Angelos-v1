# Use the official .NET 8.0 SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["VoxAngelos/VoxAngelos.csproj", "VoxAngelos/"]
RUN dotnet restore "VoxAngelos/VoxAngelos.csproj"
COPY . .
WORKDIR "/src/VoxAngelos"
RUN dotnet build "VoxAngelos.csproj" -c Release -o /app/build

# Publish the app
FROM build AS publish
RUN dotnet publish "VoxAngelos.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage: run the app
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
# Render uses the PORT environment variable
ENV ASPNETCORE_URLS=http://+:10000
ENTRYPOINT ["dotnet", "VoxAngelos.dll"]
