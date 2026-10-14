# Архитектура IPA Desktop

Проект использует C# 5, WPF и .NET Framework 4.6+. Изменения 0.1.11 разделяют обязанности классов, сохраняя поведение интерфейса и формат настроек.

| Папка | Обязанность | Основные классы |
|---|---|---|
| Source/Domain | Данные и правила выбора без зависимости от WPF | State, AppRow, LocalRow, DeviceRow, DownloadSelection, InstallationSelection, SearchSession |
| Source/Infrastructure | Операции с ОС и форматами данных | ProcessRunner, SettingsStore, ToolLocator, IpaMetadata, JsonCodec, BufferedJournal |
| Source/Services | Прикладные операции | AppToolClient, ApplicationDownloads, AppCatalog, DeviceService, AppleSupportService, LocalAppRepository, EngineUpdater, GuiUpdater |
| Source/Presentation | Отображение и обработка действий | Desktop, AccountController, SearchController, FilesController, UpdatesController, WindowController |

Desktop создаёт контроллеры, управляет временем жизни окна, журналом, отменой и состоянием текущей операции. Контроллер каждой вкладки хранит её выбор и подключает обработчики кнопок. Для операций с файлами и устройствами контроллеры обращаются к сервисам.

Engine — фасад: собирает сервисы и сохраняет существующий API адаптера. Запуск процесса реализован через IProcessRunner, команды ipatool — через IAppToolClient. Их можно подменить при проверках без настоящего Apple Account или iPhone.

Модели состояния совместимы с state.json прежних версий. Имена свойств release/asset соответствуют GitHub API. Проверка обновлений GUI сохраняется при установке и откате движка.

## Изменение и сборка

1. Изменить класс в Source.
2. Выполнить powershell -NoProfile -ExecutionPolicy Bypass -File Sync-Sources.ps1.
3. Выполнить Build.cmd.
4. Запустить Verify.ps1, Verify-IpaMetadata.ps1, Verify-Updater.ps1, Verify-Design.ps1 и Verify-Services.ps1. WPF-проверки запускать в Windows PowerShell с -STA.
5. Для выпуска повысить GuiUpdater.CurrentVersion и версию в MainWindow.xaml, добавить changelogs/vX.Y.Z.md и синхронизировать исходники.

Корневые Core.cs, App.cs, IpaMetadata.cs, WindowTheme.cs и GuiUpdater.cs генерируются из Source. Их можно компилировать прежним Build.cmd без дополнительных пакетов. Это сохраняет совместимость с обновлением 0.1.6–0.1.10, которое заменяет фиксированный набор файлов.

CI проверяет совпадение генерируемых копий с Source перед сборкой. В готовом ZIP находятся эти копии; для разработки используется репозиторий.

## Проверки

Существующие сценарии проверяют очереди, процессы, отмену, метаданные, обновление/откат, WPF-шаблоны, крестики, журнал и размер окна. Verify-Services.ps1 добавляет проверки с RecordingProcessRunner и проверяет реальные контроллеры окна с отключённым запуском сетевых проверок. Подмена возвращает тестовые ответы и не запускает Apple-утилиты.

AppleSupportService получает данные через IAppleSupportProbe. WindowsAppleSupportProbe только читает список служб и 32-/64-битный реестр; он не запускает службы и не устанавливает драйверы. Проверки различают отсутствие компонентов и невозможность прочитать их состояние. В тестах поставщик подменяется, поэтому установка Apple ПО на сервере сборки не требуется.
