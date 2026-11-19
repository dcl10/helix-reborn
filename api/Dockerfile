# syntax=docker/dockerfile:1
#
# The .NET API. Build from the repo root:
#   docker build -f api/Dockerfile .                    the API (the default, last stage)
#   docker build -f api/Dockerfile --target migrate .   a one-shot image that applies the EF Core migrations

ARG DOTNET_VERSION=10.0

# ── Build and publish the API ──
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src/api
COPY api/Directory.Build.props api/Directory.Packages.props api/HelixReborn.slnx ./
COPY api/.config ./.config
COPY api/src/HelixReborn.Api/HelixReborn.Api.csproj src/HelixReborn.Api/
RUN dotnet restore src/HelixReborn.Api/HelixReborn.Api.csproj
COPY api/src ./src
RUN dotnet publish src/HelixReborn.Api/HelixReborn.Api.csproj -c Release -o /out/app --no-restore

# ── EF Core migrations bundle: migrations run as their own step, never at API startup ──
FROM build AS bundle
RUN dotnet tool restore \
 && dotnet ef migrations bundle --project src/HelixReborn.Api --configuration Release -o /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS migrate
COPY --from=bundle /out/efbundle /efbundle
USER app
# Reads ConnectionStrings__DefaultConnection, like the API.
ENTRYPOINT ["/efbundle"]

# ── The API ──
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS api
ARG HELIX_VERSION=dev
WORKDIR /app
COPY --from=build /out/app ./
ENV Helix__Version=$HELIX_VERSION
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "HelixReborn.Api.dll"]
