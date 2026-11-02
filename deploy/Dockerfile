FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY IBTrade.sln ./
COPY src/ ./src/
RUN dotnet publish src/IBTrade.Api/IBTrade.Api.csproj -c Release -o /out --nologo

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out ./
COPY deploy/api-entrypoint.sh /entrypoint.sh
RUN chmod 755 /entrypoint.sh \
    && mkdir -p /data \
    && chown app:app /data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8090 \
    IBTRADE_DATA_DIR=/data \
    TWS__Enabled=false \
    TWS__Mode=Paper
EXPOSE 8090
ENTRYPOINT ["/entrypoint.sh"]
