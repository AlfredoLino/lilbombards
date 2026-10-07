FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY LBRelay.csproj .
RUN dotnet restore
COPY Program.cs .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
ENV RELAY_PORT=7777
EXPOSE 7777/tcp
USER $APP_UID
ENTRYPOINT ["dotnet", "LBRelay.dll"]