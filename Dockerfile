# Imagen de la app Amigo Secreto (Blazor .NET 10)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/AmigoSecreto/AmigoSecreto.csproj src/AmigoSecreto/
RUN dotnet restore src/AmigoSecreto/AmigoSecreto.csproj
COPY src/ src/
RUN dotnet publish src/AmigoSecreto/AmigoSecreto.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
USER root
RUN mkdir -p /llaves && chown app:app /llaves
USER app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__Ruta=/llaves
EXPOSE 8080
ENTRYPOINT ["dotnet", "AmigoSecreto.dll"]
