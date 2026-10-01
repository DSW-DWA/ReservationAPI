FROM mcr.microsoft.com/dotnet/sdk:8.0@sha256:78235e09001f52b6592c458ac010775ebac6725422e80cd0c1650590f67b2743 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY RestaurantSeating.Api/RestaurantSeating.Api.csproj RestaurantSeating.Api/
RUN dotnet restore RestaurantSeating.Api/RestaurantSeating.Api.csproj
COPY RestaurantSeating.Api/ RestaurantSeating.Api/
RUN dotnet publish RestaurantSeating.Api/RestaurantSeating.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0@sha256:2f202e1169ec507bdc07007cf68c14d0ff3a098110b17c460a60185e1f36a9d1
WORKDIR /app
COPY --from=build /app .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "RestaurantSeating.Api.dll"]
