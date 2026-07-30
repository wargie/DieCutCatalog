# Разработка Android-клиента

Android-приложение находится в `src/DieCutCatalog.Mobile` и собирается отдельным решением `DieCutCatalog.Mobile.sln`. Основное решение `DieCutCatalog.sln` не включает мобильный проект, поэтому сборка ПК-клиента и сервера не требует Android SDK.

## Worktree

- основной проект: `DieCutCatalog`, ветка `main`;
- Android-проект: `DieCutCatalog-Android`, короткая ветка `codex/android-bootstrap`.

Перед объединением Android-ветка обновляется из `main`, проверяется и затем вливается обратно в `main`.

## Требования

- .NET SDK 9;
- workload `maui-android`;
- Microsoft OpenJDK 17;
- Android SDK API 35;
- минимальная версия устройства: Android 10 (API 29).

## Сборка на текущем компьютере

Основная команда:

```powershell
.\scripts\build-android.ps1
```

Android `aapt2` не работает с кириллицей в полном пути проекта. Скрипт автоматически подключает worktree временной буквой диска. Эквивалентная ручная последовательность:

```powershell
subst R: "D:\Вырубка\Rotazija\Каталог ножей\DieCutCatalog-Android"
Set-Location R:\
dotnet build DieCutCatalog.Mobile.sln `
  -p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk" `
  -p:JavaSdkDirectory="C:\Program Files\Microsoft\jdk-17.0.20.8-hotspot"
Set-Location C:\
subst R: /d
```

## Текущий этап

Версия `0.1.0` содержит локальный GUI-каркас:

- экран входа;
- каталог с поиском и фильтром оборудования;
- переключение компактного и подробного представления;
- карточку ножа;
- ввод тиража и предварительный расчёт пробега и оборотов;
- экран профиля-заглушку.

Авторизация, каталог и запись тиража пока работают на демонстрационных данных. Подключение к production API выполняется отдельной функциональной веткой.