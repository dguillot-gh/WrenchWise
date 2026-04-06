FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["CarCareHybrid.Backend/CarCareHybrid.Backend.csproj", "CarCareHybrid.Backend/"]
COPY ["CarCareHybrid.Shared/CarCareHybrid.Shared.csproj", "CarCareHybrid.Shared/"]
RUN dotnet restore "CarCareHybrid.Backend/CarCareHybrid.Backend.csproj"

COPY . .
WORKDIR "/src/CarCareHybrid.Backend"
RUN dotnet publish "CarCareHybrid.Backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CarCareHybrid.Backend.dll"]
