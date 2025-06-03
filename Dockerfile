# === ЭТАП 1: СБОРКА ПРИЛОЖЕНИЯ ===
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env
WORKDIR /src

# Копируем все файлы из контекста (папки StackOverStadyApi) в /src контейнера
COPY . .

# Восстанавливаем зависимости для всего солюшена (или конкретного проекта)
# Замени MySolution.sln на имя твоего файла .sln
# Если .sln файл называется так же, как главный .csproj, можно указать его.
# Либо, если только один проект API, можно восстановить для него:
# RUN dotnet restore StackOverStadyApi.csproj
# Если нет .sln, а только .csproj
RUN dotnet restore StackOverStadyApi.csproj

# Публикуем приложение API.
# Замени StackOverStadyApi.csproj на имя твоего .csproj файла API
RUN dotnet publish StackOverStadyApi.csproj -c Release -o /app/publish --no-restore

# === ЭТАП 2: СОЗДАНИЕ ОБРАЗА ДЛЯ ЗАПУСКА ===
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build-env /app/publish .

EXPOSE 8080

# Замени StackOverStadyApi.dll на имя твоей DLL
ENTRYPOINT ["dotnet", "StackOverStadyApi.dll"]