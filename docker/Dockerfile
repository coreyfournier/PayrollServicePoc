FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["src/PayrollService.Api/PayrollService.Api.csproj", "PayrollService.Api/"]
COPY ["src/PayrollService.Application/PayrollService.Application.csproj", "PayrollService.Application/"]
COPY ["src/PayrollService.Domain/PayrollService.Domain.csproj", "PayrollService.Domain/"]
COPY ["src/PayrollService.Infrastructure/PayrollService.Infrastructure.csproj", "PayrollService.Infrastructure/"]
RUN dotnet restore "PayrollService.Api/PayrollService.Api.csproj"
COPY src/ .
WORKDIR "/src/PayrollService.Api"
RUN dotnet build "PayrollService.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PayrollService.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PayrollService.Api.dll"]
