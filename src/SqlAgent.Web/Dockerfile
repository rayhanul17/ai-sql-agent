# ============================================================
#  Multi-stage build for the SqlAgent.Web app.
#  Build context is the repository root (see docker-compose.full.yml).
# ============================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (better layer caching).
COPY global.json ./
COPY src/SqlAgent.Domain/SqlAgent.Domain.csproj              src/SqlAgent.Domain/
COPY src/SqlAgent.Application/SqlAgent.Application.csproj     src/SqlAgent.Application/
COPY src/SqlAgent.Infrastructure/SqlAgent.Infrastructure.csproj src/SqlAgent.Infrastructure/
COPY src/SqlAgent.Web/SqlAgent.Web.csproj                     src/SqlAgent.Web/
RUN dotnet restore src/SqlAgent.Web/SqlAgent.Web.csproj

# Copy the rest and publish.
COPY . .
RUN dotnet publish src/SqlAgent.Web/SqlAgent.Web.csproj -c Release -o /app /p:UseAppHost=false

# ---- Runtime image ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SqlAgent.Web.dll"]
