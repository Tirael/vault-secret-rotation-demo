FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY PostgresVaultService.sln ./
COPY src/PostgresVaultService/PostgresVaultService.csproj src/PostgresVaultService/
RUN dotnet restore src/PostgresVaultService/PostgresVaultService.csproj
COPY src/PostgresVaultService/ src/PostgresVaultService/
RUN dotnet publish src/PostgresVaultService/PostgresVaultService.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PostgresVaultService.dll"]
