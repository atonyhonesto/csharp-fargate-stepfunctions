# Build with the SDK image, run on the small runtime image. Multi-arch: works for x64 and Graviton (ARM64) Fargate.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/LapReport/LapReport.csproj src/LapReport/
RUN dotnet restore src/LapReport/LapReport.csproj
COPY src/LapReport/ src/LapReport/
RUN dotnet publish src/LapReport/LapReport.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
# The .NET images ship a non-root 'app' user (UID 1654).
USER app
ENTRYPOINT ["dotnet", "LapReport.dll"]
