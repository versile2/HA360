# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig Realm.slnx ./
COPY src/Realm.Domain/Realm.Domain.csproj               src/Realm.Domain/
COPY src/Realm.Infrastructure/Realm.Infrastructure.csproj src/Realm.Infrastructure/
COPY src/Realm.Demo/Realm.Demo.csproj                   src/Realm.Demo/
COPY src/Realm.Web/Realm.Web.csproj                     src/Realm.Web/
RUN dotnet restore src/Realm.Web/Realm.Web.csproj
COPY src/ src/
ARG VERSION=0.0.0-dev
ARG REVISION=unknown
RUN dotnet publish src/Realm.Web/Realm.Web.csproj -c Release -o /out --no-restore \
      -p:UseAppHost=false -p:DebugType=none -p:Version=${VERSION} -p:ContinuousIntegrationBuild=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ARG VERSION=0.0.0-dev
ARG REVISION=unknown
ENV ASPNETCORE_HTTP_PORTS=8099 \
    DOTNET_gcServer=0 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_TieredPGO=0 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0
LABEL org.opencontainers.image.title="The Realm" \
      org.opencontainers.image.description="Life360-style family map for Home Assistant" \
      org.opencontainers.image.source="https://github.com/Versile2/ha360" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}" \
      io.hass.type="addon" io.hass.name="The Realm" io.hass.version="${VERSION}" \
      io.hass.description="Life360-style family map for Home Assistant" \
      io.hass.url="https://github.com/Versile2/ha360"
EXPOSE 8099
ENTRYPOINT ["dotnet", "Realm.Web.dll"]
