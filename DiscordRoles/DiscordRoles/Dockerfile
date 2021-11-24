#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["DiscordRoles/DiscordRoles.csproj", "DiscordRoles/"]
RUN dotnet restore "DiscordRoles/DiscordRoles.csproj"
COPY . .
WORKDIR "/src/DiscordRoles"
RUN dotnet build "DiscordRoles.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DiscordRoles.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DiscordRoles.dll"]