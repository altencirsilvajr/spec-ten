FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/SpecTen.Web/SpecTen.Web.csproj src/SpecTen.Web/
RUN dotnet restore src/SpecTen.Web/SpecTen.Web.csproj

COPY src/SpecTen.Web/ src/SpecTen.Web/
RUN dotnet publish src/SpecTen.Web/SpecTen.Web.csproj -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SpecTen.Web.dll"]
