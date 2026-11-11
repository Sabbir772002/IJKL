FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY . .
RUN dotnet restore src/SchoolAdmission.Api
RUN dotnet publish src/SchoolAdmission.Api -c Release --no-restore -o /publish

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS test
WORKDIR /app
COPY . .
RUN dotnet build SchoolAdmission.slnx -c Release
ENTRYPOINT ["dotnet", "test", "SchoolAdmission.slnx", "-c", "Release", "--no-build"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SchoolAdmission.Api.dll"]
