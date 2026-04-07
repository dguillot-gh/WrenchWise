FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["WrenchWise.Backend/WrenchWise.Backend.csproj", "WrenchWise.Backend/"]
COPY ["WrenchWise.Shared/WrenchWise.Shared.csproj", "WrenchWise.Shared/"]
RUN dotnet restore "WrenchWise.Backend/WrenchWise.Backend.csproj"

COPY . .
WORKDIR "/src/WrenchWise.Backend"
RUN dotnet publish "WrenchWise.Backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "WrenchWise.Backend.dll"]
