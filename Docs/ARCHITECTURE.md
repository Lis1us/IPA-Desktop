# Архитектура IPA Desktop

Проект использует C# 5, WPF и .NET Framework 4.6+. Архитектура разделяет обязанности классов, сохраняя поведение интерфейса и формат настроек.

| Папка | Обязанность | Основные классы |
|---|---|---|
| ../Source/Domain | Данные и правила выбора без зависимости от WPF | State, AppRow, LocalRow, DeviceRow, DownloadSelection, InstallationSelection, SearchSession |
| ../Source/Infrastructure | Операции с ОС и форматами данных | ProcessRunner, SettingsStore, ToolLocator, IpaMetadata, JsonCodec, BufferedJournal |
| ../Source/Services | Прикладные операции | AppToolClient, ApplicationDownloads, AppCatalog, DeviceService, AppleSupportService, LocalAppRepository, EngineUpdater, GuiUpdater |
| ../Source/Presentation | Отображение и обработка действий | Desktop, AccountController, SearchController, FilesController, UpdatesController, WindowController |

Desktop создаёт контроллеры, управляет временем жизни окна, журналом, отменой и состоянием текущей операции. Контроллер каждой вкладки хранит её выбор и подключает обработчики кнопок. Для операций с файлами и устройствами контроллеры обращаются к сервисам.

Engine — фасад: собирает сервисы и сохраняет существующий API адаптера. Запуск процесса реализован через IProcessRunner, команды ipatool — через IAppToolClient. Их можно подменить при проверках без настоящего Apple Account или iPhone.

Модели состояния совместимы с state.json прежних версий. Имена свойств release/asset соответствуют GitHub API. Проверка обновлений GUI сохраняется при установке и откате движка.

## Источники инструментов

EngineUpdater получает Windows x64 ZIP напрямую из Releases Sorvigolova/ipatool и проверяет запуск CLI до переключения настроек. State.Source и PreviousSource различают источники даже при совпадении номеров версий. Старые настройки без этих полей продолжают работать с прежним комплектом.

DeviceCurrent и DeviceVersion указывают отдельный комплект для установки на iPhone. При миграции используется существующий комплект; при чистой установке из IPA_Downloader копируются только три Windows EXE и DLL из их папки. Обновление и откат ipatool сохраняют DeviceCurrent. AppCatalog независимо получает AppsList.txt из IPA_Downloader и использует локальную копию при ошибке сети.

Verify-Engine.ps1 проверяет разделение источников, избирательный импорт, сохранение настроек и откат. С -Live он скачивает настоящие опубликованные Windows-архивы и запускает только справку ipatool, без входа в Apple Account и скачивания приложений.

## Изменение и сборка

1. Изменить класс в Source.
2. Выполнить powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Sync-Sources.ps1.
3. Выполнить Build.cmd.
4. Запустить проверки из Tests: Verify.ps1, Verify-IpaMetadata.ps1, Verify-Updater.ps1, Verify-Design.ps1, Verify-Services.ps1, Verify-Engine.ps1 и Verify-Layout.ps1. Для проверки структуры предварительно создать ZIP через Scripts/Package.ps1. WPF-проверки запускать в Windows PowerShell с -STA.
5. Для выпуска повысить GuiUpdater.CurrentVersion и версию в MainWindow.xaml, добавить changelogs/vX.Y.Z.md и синхронизировать исходники.

Файлы Generated/Core.cs, Generated/App.cs, Generated/IpaMetadata.cs, Generated/WindowTheme.cs и Generated/GuiUpdater.cs генерируются из Source. Их можно компилировать прежним Build.cmd без дополнительных пакетов. Для совместимости с обновлением 0.1.6–0.1.12 ZIP содержит служебные копии прежних путей. При первом запуске InstallationLayout раскладывает ресурсы по новым папкам и убирает только копии, совпадающие с содержимым текущей сборки. Изменённые пользователем файлы сохраняются. Следующие обновления работают напрямую с новой структурой.

CI проверяет совпадение генерируемых копий с Source перед сборкой. В готовом ZIP находятся эти копии; для разработки используется репозиторий.

## Проверки

Существующие сценарии проверяют очереди, процессы, отмену, метаданные, обновление/откат, WPF-шаблоны, крестики, журнал и размер окна. Verify-Services.ps1 добавляет проверки с RecordingProcessRunner и проверяет реальные контроллеры окна с отключённым запуском сетевых проверок. Подмена возвращает тестовые ответы и не запускает Apple-утилиты.

AppleSupportService получает данные через IAppleSupportProbe. WindowsAppleSupportProbe только читает список служб и 32-/64-битный реестр; он не запускает службы и не устанавливает драйверы. Проверки различают отсутствие компонентов и невозможность прочитать их состояние. В тестах поставщик подменяется, поэтому установка Apple ПО на сервере сборки не требуется.

## Структура поставки

Assets — иконки; Tests — проверки и фикстуры; Scripts — служебные скрипты; Docs — документация; Generated — переносимые файлы компиляции. В корне остаются EXE, конфигурация, XAML, Build.cmd, Start.cmd, README и лицензия. Source хранится в репозитории для разработки. Встроенные ресурсы обеспечивают переход со старого updater, который извлекает только корневые пути. Package.ps1 формирует ZIP и совместимые копии. Verify-Layout.ps1 проверяет старый updater, миграцию, повторный запуск, сохранение изменённых файлов и компиляцию перенесённой папки.
