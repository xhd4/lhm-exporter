# syntax=docker/dockerfile:1.6

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

ARG TARGETRID=win-x64
ARG CONFIG=Release

WORKDIR /src

# Cache restore layer
COPY Directory.Build.props .
COPY src/lhm-exporter/lhm-exporter.csproj src/lhm-exporter/
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore src/lhm-exporter/lhm-exporter.csproj -r ${TARGETRID}

COPY . .

RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/lhm-exporter/lhm-exporter.csproj \
      -c ${CONFIG} \
      -r ${TARGETRID} \
      -o /out \
      --no-restore

FROM scratch AS artifact
COPY --from=build /out/ /
