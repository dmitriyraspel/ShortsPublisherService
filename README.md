# ShortsPublisherService

Сервис публикует одно видео в YouTube Shorts за запуск через Android UI automation (Appium) и ADB. 

** Выполнение может создать реальную публикацию.

## Требования

- .NET 8 SDK (или включённый в проект локальный `tools/dotnet/dotnet.exe`)
- Android SDK Platform Tools (`adb.exe`)
- Запущенный Android-эмулятор/устройство с YouTube
- Appium и UiAutomator2 driver

## Запуск

Проверенный вариант в PowerShell:

```powershell
& "C:\ShortsPublisher\tools\dotnet\dotnet.exe" run -- `
  --file ".\videos\video_2026-10-22_13-40-02.mp4" `
  --caption "Dany jumps and howls" `
  --publish-video-youtube
```

`--publish-video-youtube` оставлен для совместимости со старым запуском, но теперь публикация — основной режим и флаг необязателен.

Общий вариант:

```powershell
dotnet run -- --file "C:\Videos\clip.mp4" --caption "Caption" --task-id task-123 --result-file "C:\Results\task-123.json"
```

Параметры:
- `--file` — обязателен; поддерживаются `.mp4`, `.mov`, `.m4v`.
- `--caption` — необязателен. Если не задан, используется `Caption`, затем `Title` из соседнего `clip.json`, затем имя файла без расширения.
- `--task-id` — необязательный ID задачи.
- `--result-file` — необязательный путь к JSON-результату.

Пример `clip.json`:

```json
{
  "Caption": "Caption for the Short"
}
```

UI-поток использует проверенные настройки Public visibility и Not made for kids. Sidecar-поля не меняют эти UI-настройки.

## Результат и коды выхода

Сервис печатает один JSON-объект в stdout и при наличии `--result-file` сохраняет его в файл. Технические сообщения выводятся в stderr.

- `0` — публикация подтверждена явными сигналами YouTube.
- `1` — ошибка, ошибка загрузки или публикация не подтверждена.
- `2` — неверные аргументы.

Возможные статусы: `published`, `upload_failed`, `publication_unconfirmed`, `publication_unknown`, `failed`, `invalid_arguments`. Не повторяйте автоматически задачу с неоднозначным результатом: сначала проверьте YouTube, чтобы не создать дубликат.

### Чтобы сохранить XML текущего экрана эмулятора:

```powershell
& $DOTNET run -- --diagnose-ui
```
=====================

## Установка и Сборка

### 1. Откройте папку проекта

# Установить зависимости проекта по lock-файлу
npm ci

# Хранить расширения Appium внутри проекта
$env:APPIUM_HOME = "$PWD\.appium"

# Установить драйвер Android
npx appium driver install uiautomator2

# Проверить установленный драйвер
npx appium driver list --installed

# Если .NET не установлен глобально
```powershell
New-Item -ItemType Directory -Force -Path ".\tools\dotnet" | Out-Null
```

```powershell
Invoke-WebRequest `
  https://dot.net/v1/dotnet-install.ps1 `
  -OutFile ".\tools\dotnet-install.ps1"
```

```powershell
& "$PWD\tools\dotnet-install.ps1" `
  -Channel 8.0 `
  -InstallDir "$PWD\tools\dotnet" `
  -NoPath
```

```powershell
& "$PWD\tools\dotnet\dotnet.exe" --version
```

# Задать переменные окружения

Если Android SDK установлен в стандартной папке пользователя:

```powershell
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
$env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
$env:PATH = "$env:ANDROID_HOME\platform-tools;$env:ANDROID_HOME\emulator;$env:PATH"
$DOTNET = "$PWD\tools\dotnet\dotnet.exe"
```

# Сборка и запуск

# Запустите сервер Appium в отдельном окне PowerShell 

Проверка

```powershell
npx appium --version
npx --no-install appium driver list --installed
```

```powershell
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
$env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
$env:PATH = "$env:ANDROID_HOME\platform-tools;$env:ANDROID_HOME\emulator;$env:PATH"
$DOTNET = "$PWD\tools\dotnet\dotnet.exe"
```

Запустите локальный Appium

```powershell
npx appium --address 127.0.0.1 --port 4723
```

# Соберите проект

В основном окне PowerShell:

```powershell
& $DOTNET restore
& $DOTNET build
```

Проверьте подключение эмулятора:

```powershell
adb devices
```

Убедитесь, что эмулятор запущен и отображается со статусом `device`.



## Experimental Instagram file handoff

Run `dotnet run -- --file ".\\videos\\clip.mp4" --instagram-open-video` to verify the local and Android SHA-256 values, locate a uniquely named MediaStore entry, and attempt an Android ACTION_SEND to Instagram. The command saves UI XML in `diagnostics/` and **never publishes**. A successful exit means the handoff was attempted, **not** that Instagram selected the correct video. Inspect the emulator and XML. The Android device must support `sha256sum` and MediaStore queries.


## TikTok (в разработке)

Работа ведётся в ветке `feature/tiktok`. Сначала установите TikTok и войдите в аккаунт на Android-эмуляторе.

Открыть TikTok без публикации:

```powershell
& $DOTNET run -- --open-tiktok
```

Сохранить XML текущего экрана TikTok (приложение должно быть открыто):

```powershell
& $DOTNET run -- --diagnose-ui
```

Каркас публикации, **пока ничего не публикует**:

```powershell
& $DOTNET run -- --file ".\\videos\\new-video.mp4" --caption "Caption #tiktok" --publish-video-tiktok
```

Команда проверяет входной файл и подпись, открывает TikTok и возвращает `tiktok_not_implemented` с `Success=false`, `Published=false` и кодом выхода 1. Загрузка файла в TikTok, нажатие Post и подтверждение публикации пока не реализованы. Этапы добавляются после проверки XML интерфейса. Пакет TikTok по умолчанию: `com.zhiliaoapp.musically`; если установлена другая версия приложения, потребуется скорректировать идентификатор пакета.
