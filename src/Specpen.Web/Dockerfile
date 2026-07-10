FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Specpen.Web/Specpen.Web.csproj src/Specpen.Web/
RUN dotnet restore src/Specpen.Web/Specpen.Web.csproj

COPY src/Specpen.Web/ src/Specpen.Web/
RUN dotnet publish src/Specpen.Web/Specpen.Web.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Specpen.Web.dll"]
