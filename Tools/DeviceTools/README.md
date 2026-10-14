# IPA Desktop Device Tools

Windows 10/11 x64-комплект для IPA Desktop, собранный нами из исходников проектов libimobiledevice. Это собственная сборка сторонних свободных проектов, а не новая реализация протокола Apple.

В комплекте: idevice_id, ideviceinfo, ideviceinstaller и только необходимые DLL. Подключение по USB на Windows использует Apple Mobile Device Service; установите Apple Devices/iTunes и подтвердите доверие на iPhone.

## Исходники и лицензии

Исходные ревизии зафиксированы в sources.json (в репозитории — pins.json). Лицензии проектов и зависимостей MSYS2 находятся в LICENSES. Исходники шести собираемых проектов опубликованы в архиве Sources рядом с бинарным ZIP. Компоненты сохраняют собственные лицензии, включая LGPL/GPL; лицензия MIT интерфейса на них не распространяется.

- https://github.com/libimobiledevice/libplist
- https://github.com/libimobiledevice/libimobiledevice-glue
- https://github.com/libimobiledevice/libusbmuxd
- https://github.com/libimobiledevice/libtatsu
- https://github.com/libimobiledevice/libimobiledevice
- https://github.com/libimobiledevice/ideviceinstaller

Версии системных зависимостей и компилятора перечислены в msys2-packages.txt. Зависимости MinGW получены из MSYS2; рецепты и ссылки на исходники: https://github.com/msys2/MINGW-packages и https://packages.msys2.org/. Исходные пакеты: https://repo.msys2.org/mingw/sources/. Официальный архив пакетов: https://repo.msys2.org/mingw/mingw64/.

## Повторная сборка

См. .github/workflows/device-tools.yml в https://github.com/Lis1us/IPA-Desktop. В окружении MSYS2 MINGW64 установите перечисленные в workflow пакеты, затем запустите bash Tools/DeviceTools/build.sh из корня репозитория. Комплект проверяется после удаления MinGW из PATH; фактическое обнаружение и установка на iPhone требуют отдельной проверки с устройством.
