# Multi-target Dockerfile for the .NET services: --target api | pricemock | parser
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY GridMeter.sln ./
COPY src/ src/
COPY tests/ tests/
RUN dotnet publish src/GridMeter.Api -c Release -o /out/api --nologo \
 && dotnet publish src/GridMeter.PriceMock -c Release -o /out/pricemock --nologo \
 && dotnet publish src/GridMeter.MeterParser.LocalRunner -c Release -o /out/parser --nologo

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /out/api .
EXPOSE 8080
ENTRYPOINT ["dotnet", "GridMeter.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS pricemock
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /out/pricemock .
EXPOSE 8080
ENTRYPOINT ["dotnet", "GridMeter.PriceMock.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS parser
WORKDIR /app
COPY --from=build /out/parser .
ENTRYPOINT ["dotnet", "GridMeter.MeterParser.LocalRunner.dll"]
