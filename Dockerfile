# Recommendation service (:5056). No database of its own — Redis, plus HTTP to the article,
# faiss and ranking services — so nothing here needs a volume or a migration step.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# csproj first so a source-only change reuses the restore layer.
COPY explAInedRecommendationService.csproj ./
RUN dotnet restore explAInedRecommendationService.csproj

COPY . .
RUN dotnet publish explAInedRecommendationService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish ./

# Same port inside the container as on the host, so the service map in CLAUDE.md keeps
# meaning the same thing under a 1:1 compose mapping.
ENV ASPNETCORE_HTTP_PORTS=5056
EXPOSE 5056

USER $APP_UID
ENTRYPOINT ["dotnet", "explAInedRecommendationService.dll"]
