# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig Realm.slnx ./
COPY src/Realm.Domain/Realm.Domain.csproj               src/Realm.Domain/
COPY src/Realm.Infrastructure/Realm.Infrastructure.csproj src/Realm.Infrastructure/
COPY src/Realm.Demo/Realm.Demo.csproj                   src/Realm.Demo/
COPY src/Realm.Web/Realm.Web.csproj                     src/Realm.Web/
# The SDK adds Microsoft.AspNetCore.App.Internal.Assets (the package that holds _framework/blazor.web.js) only to a project that
# contains a .razor file, and only restore can fetch it: publish below runs --no-restore. This layer holds the csproj files alone,
# so a stub stands in for the real components while restoring and is gone again at the end of the same layer. Without it the image
# serves a 404 for the Blazor script (S2 fix 1). <RequiresAspNetWebAssets>true</RequiresAspNetWebAssets> in Realm.Web.csproj
# would make the stub unnecessary.
# D63: the image is built for linux-x64 only (config.yaml: arch amd64), so restore and publish both name that runtime. A RID-specific publish
# keeps only the native libraries of linux-x64 (EF Core's SQLite ships one per platform, which made the app layer 52.7 MB). Restore needs the
# same -r and properties as publish: publish below runs --no-restore, and an assets file without a linux-x64 target fails it with NETSDK1047.
# The runtime stays framework-dependent (the aspnet base image supplies it); ReadyToRun and trimming stay off (03 section 6.3).
RUN touch src/Realm.Web/RestoreStub.razor \
 && dotnet restore src/Realm.Web/Realm.Web.csproj -r linux-x64 -p:SelfContained=false -p:UseAppHost=false \
 && rm src/Realm.Web/RestoreStub.razor
COPY src/ src/
ARG VERSION=0.0.0-dev
ARG REVISION=unknown
RUN dotnet publish src/Realm.Web/Realm.Web.csproj -c Release -o /out --no-restore -r linux-x64 --self-contained false \
      -p:UseAppHost=false -p:DebugType=none -p:Version=${VERSION} -p:ContinuousIntegrationBuild=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
# The licence of this project, the third-party notices and the licence texts they point to ship in the image (tools/ci/image-smoke.sh item 12).
COPY LICENSE THIRD-PARTY-NOTICES.md /app/
COPY LICENSES/ /app/LICENSES/
ARG VERSION=0.0.0-dev
ARG REVISION=unknown
ENV ASPNETCORE_HTTP_PORTS=8099 \
    DOTNET_gcServer=0 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_TieredPGO=0 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=0
LABEL org.opencontainers.image.title="The Realm" \
      org.opencontainers.image.description="Map-first family map and weekly driving reports for Home Assistant" \
      org.opencontainers.image.source="https://github.com/Versile2/ha360" \
      org.opencontainers.image.licenses="MIT AND BSD-3-Clause AND Apache-2.0 AND OFL-1.1" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${REVISION}" \
      io.hass.type="addon" io.hass.name="The Realm" io.hass.version="${VERSION}" \
      io.hass.description="Map-first family map and weekly driving reports for Home Assistant" \
      io.hass.url="https://github.com/Versile2/ha360"
EXPOSE 8099
ENTRYPOINT ["dotnet", "Realm.Web.dll"]
