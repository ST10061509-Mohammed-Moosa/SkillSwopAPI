# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY SkillSwopApi.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Render/Railway route external HTTP traffic to whatever port your app listens on,
# injected as the PORT env var at container *runtime* (not build time) — so it has
# to be read in the entrypoint shell, not baked into an ENV line.
EXPOSE 8080
ENTRYPOINT ["/bin/sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} dotnet SkillSwopApi.dll"]
