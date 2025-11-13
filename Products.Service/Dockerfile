FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Products.Service.csproj", "./"]
RUN dotnet restore "Products.Service.csproj"

COPY . .
RUN dotnet build "Products.Service.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Products.Service.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

EXPOSE 80
ENTRYPOINT ["dotnet", "Products.Service.dll"]
