# ── 建置階段 ──
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Yuyu.Api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

# ── 執行階段 ──
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# 不要把 appsettings.json 的密鑰打進 image；正式環境改用環境變數注入
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Yuyu.Api.dll"]
