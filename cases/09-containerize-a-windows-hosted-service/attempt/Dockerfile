# The first attempt: the application exactly as it is, in the smallest official image.
# Kept in the repository because the tests are run against it too, to show what fails. Build from the case folder:
#   docker build -f attempt/Dockerfile -t inbox:attempt .
ARG BASE=10.0-alpine
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY before/src/Inbox/ .
RUN dotnet publish -c Release -o /app

# BASE=10.0 selects the default image instead, which has culture and time zone data:
#   docker build -f attempt/Dockerfile --build-arg BASE=10.0 -t inbox:attempt-default .
ARG BASE
FROM mcr.microsoft.com/dotnet/aspnet:${BASE}
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT dotnet Inbox.dll
