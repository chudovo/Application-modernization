# Build from the case folder:  docker build -f after/Dockerfile -t inbox:after .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY after/src/Inbox/ .
RUN dotnet publish -c Release -o /app
# The final image has no shell, so the data folder is prepared here and copied with its owner.
RUN mkdir /data

# "chiseled": no shell, no package manager, runs as a non-root user. "extra": includes the culture data (ICU) and the
# time zone database, which the plain chiseled and the alpine images leave out.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
COPY --from=build --chown=$APP_UID:$APP_UID /data /data
VOLUME /data
USER $APP_UID
EXPOSE 8080
# Exec form: the application is process 1 and receives SIGTERM itself. The shell form ("ENTRYPOINT dotnet Inbox.dll")
# puts a shell in between that does not pass the signal on.
ENTRYPOINT ["dotnet", "Inbox.dll"]
