FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["WrenchWise.Web/WrenchWise.Web.csproj", "WrenchWise.Web/"]
COPY ["WrenchWise.Shared/WrenchWise.Shared.csproj", "WrenchWise.Shared/"]
RUN dotnet restore "./WrenchWise.Web/WrenchWise.Web.csproj"
COPY . .
WORKDIR "/src/WrenchWise.Web"
RUN dotnet build "./WrenchWise.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./WrenchWise.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WrenchWise.Web.dll"]
