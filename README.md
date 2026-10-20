# ShortsPublisherService

## Current behavior

- Validates the input path, extension and non-empty file.
- Reads optional sidecar metadata (`clip.json` for `clip.mp4`).
- Checks that the configured Android device is connected through ADB.
- Copies the video into the emulator using ADB.
- Emits one JSON result to standard output and returns a process exit code.
- Optionally writes the same result to `--result-file`.

**Important:** `DryRun=true` means validation and transfer only. The result status is `dry_run_completed`, `success=true`, `published=false`. This is not a YouTube publication confirmation. Live upload currently fails closed because the installed YouTube UI selectors have not yet been verified and implemented. The service must not claim `published` until it verifies the final YouTube upload state.

## Requirements

- .NET 8 SDK
- Android Studio emulator or a connected Android device
- Android SDK Platform Tools (`adb.exe`)
- For future live UI automation: Node.js, Appium and UiAutomator2 driver

No database or Docker is required.

## Configure

Edit `appsettings.json`:

- `Publisher:AdbPath`: path to `adb.exe`. The default uses `%LOCALAPPDATA%`.
- `Publisher:DeviceId`: normally `emulator-5554`.
- `Publisher:DryRun`: keep `true` until live UI automation is implemented and tested.

Environment variables may override settings using the `SHORTSPUBLISHER_` prefix. Example:
`SHORTSPUBLISHER_Publisher__DeviceId=emulator-5554`

## Build

From the project directory:

```powershell
dotnet restore
dotnet build -c Release
```

## Run one task

```powershell
dotnet run -- --task-id task-123 --file "C:\ShortsPublisher\incoming\clip.mp4" --result-file "C:\ShortsPublisher\results\task-123.json"
```

After building, an external service can call the executable directly:

```powershell
.\bin\Release\net8.0\ShortsPublisher.exe --task-id task-123 --file "C:\ShortsPublisher\incoming\clip.mp4" --result-file "C:\ShortsPublisher\results\task-123.json"
```

The exact executable name/path can vary by build configuration.

## Result contract

Successful dry run:

```json
{
  "taskId": "task-123",
  "status": "dry_run_completed",
  "success": true,
  "published": false,
  "file": "C:\\ShortsPublisher\\incoming\\clip.mp4",
  "message": "Validation and ADB transfer succeeded. Dry run only: YouTube publication was not attempted.",
  "sha256": "…",
  "startedAtUtc": "2026-10-20T12:00:00+00:00",
  "completedAtUtc": "2026-10-20T12:00:02+00:00"
}
```

Failure returns `success=false`, `status=failed`, a message, and exit code `1`. Invalid arguments return exit code `2`. The external service should use the exit code for process-level success/failure and parse `status` for business meaning. It should retry only according to its own policy. In particular, a timeout after the upload button was tapped can be ambiguous and should not be blindly retried because it could create a duplicate.

## Metadata sidecar

For `clip.mp4`, an optional `clip.json` can contain:

```json
{
  "title": "Test Short",
  "description": "Test description",
  "visibility": "private",
  "madeForKids": false
}
```

## Explicit limitation

This is not yet a verified end-to-end YouTube uploader. In live mode, the automation throws `LIVE_UPLOAD_NOT_IMPLEMENTED` rather than inventing success. Implement and verify selectors for the actual YouTube app version and confirm the final upload state before changing the result to `published`.


## Запуск проекта без глобальной установки

Инструкция для Windows и PowerShell. Все команды выполняются из папки проекта.

### 1. Откройте папку проекта

# Установить зависимости проекта по lock-файлу
npm ci

# Хранить расширения Appium внутри проекта
$env:APPIUM_HOME = "$PWD\.appium"

# Установить драйвер Android
npx appium driver install uiautomator2

# Проверить установленный драйвер
npx appium driver list --installed


### 2. Настройте локальный .NET SDK

# Собрать .NET-проект локальным SDK
```powershell
& "$PWD\tools\dotnet\dotnet.exe" restore
```

```powershell
& "$PWD\tools\dotnet\dotnet.exe" build
```
Если файл существует и команда выводит версию SDK, можно продолжать, если нет:

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

### 3. Настройте Android SDK

Если Android SDK установлен в стандартной папке пользователя:

```powershell
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
$env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
$env:PATH = "$env:ANDROID_HOME\platform-tools;$env:ANDROID_HOME\emulator;$env:PATH"
```

Проверьте подключение эмулятора:

```powershell
adb devices
```

Убедитесь, что эмулятор запущен и отображается со статусом `device`.

### 4. Запустите локальный Appium

Проверка

```powershell
npx appium --version
npx --no-install appium driver list --installed
```

Запустите сервер в отдельном окне PowerShell:

```powershell
npx appium --address 127.0.0.1 --port 4723
```

> Для полностью воспроизводимого запуска сохраните `package.json` и `package-lock.json` в репозитории. Установка драйвера UiAutomator2 выполняется отдельно от установки самого Appium.

### 5. Соберите проект

В основном окне PowerShell:

```powershell
& $DOTNET restore
& $DOTNET build
```

### 6. Проверьте интерфейс YouTube

Чтобы сохранить XML текущего экрана эмулятора:

```powershell
& $DOTNET run -- --diagnose-ui
```

Результат сохраняется в файле `appium-screen.xml` в корне проекта.

### 7. Запустите нужный тест

Используйте только флаги, реализованные в `Program.cs`. Например:

```powershell
& $DOTNET run -- --test-back-from-visibility
```

Перед запуском других флагов проверьте их обработчики в `Program.cs`.

**Внимание:** тест, нажимающий `Upload Short`, выполняет реальную попытку публикации видео. Запускайте его только после проверки ролика и настроек.

### Примечания

- Команды Appium запускайте в отдельном окне, оставляя сервер работающим.
- Не устанавливайте Appium глобально.
- Не добавляйте в Git личные данные, токены, видео и диагностические XML с конфиденциальной информацией.

