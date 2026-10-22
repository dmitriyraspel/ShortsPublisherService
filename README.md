# ShortsPublisherService

Сервис публикует одно видео в YouTube Shorts за запуск через Android UI automation (Appium) и ADB. **Dry run отсутствует:** выполнение может создать реальную публикацию.

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

Снимки XML/PNG и диагностические файлы не создаются. XML-дерево UI анализируется только в памяти.

## Сборка

```powershell
dotnet restore
dotnet build --configuration Release
```

Подробнее о формате задачи и JSON-результате см. [`docs/task-contract.md`](docs/task-contract.md).
