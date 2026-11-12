# Build context: the case folder (cases/02-wcf-to-web-api), because the SOAP contract source lives in before/.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY before/src/Shipping.Contracts before/src/Shipping.Contracts
COPY after after
RUN dotnet publish after/src/Shipping.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shipping.Api.dll"]
