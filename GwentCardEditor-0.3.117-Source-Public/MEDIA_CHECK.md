# Media Package Check / Проверка медиапакетов / 媒体资源包检查

## Русский

Во вкладке «Оформление» нажмите «Проверить медиапакеты…» и подтвердите запуск.
Нужен открытый клиент GWENT без несохранённых изменений редактора, полный комплект редактора
и не менее 512 МБ свободного места во временной папке Windows. Проверка может занять несколько минут.

Редактор копирует необходимые ресурсы во временную папку и только там:
- заменяет статичный арт во всех четырёх качествах;
- устанавливает золотую рамку поверх изменения арта, оставляя бронзовую;
- переназначает штатный звук и устанавливает отдельный WAV-пакет;
- устанавливает пакет анимированной рамки для золотых карт, удаляет плагин и проверяет полный откат;
- проверяет отказ устаревшему пакету, неверному порядку отката и перезаписи чужого изменения;
- отменяет изменения в обратном порядке и сравнивает все файлы с исходной копией побайтно.

Выбранный клиент не изменяется, звук не воспроизводится, игра не запускается.
Настоящий BepInEx не нужен для этой проверки: во временной копии используются тестовые маркеры.
Успешный результат не подтверждает работу игрового плагина, MP3-кодека или совместимость с другими модами.
Проверка использует карты 202103 и 112103; если они существенно изменены в вашем клиенте,
проверка может завершиться неудачей без подтверждения совместимости.

Отчёт сохраняется в `%LOCALAPPDATA%/GwentCardEditor/Reports`; точный путь показывается после завершения.
Отчёт содержит местные пути файлов. Перед публикацией просмотрите его.
Неполная проверка не считается успешной; в отчёте указан этап ошибки.

Из практических правил: после установки одного медиапакета следующий нужно собирать на текущем
состоянии клиента. Арты и рамки используют общий каталог ресурсов; отменяйте их в обратном порядке.
После изменения `data_definitions` сначала отмените более поздний пакет звука/данных,
затем восстанавливайте более раннее оформление. Редактор не перезаписывает конфликтующие файлы.

## English

Open Appearance, choose Check Media Packages, and confirm. Requires a complete editor distribution,
an open GWENT client without unsaved editor changes, and at least 512 MB free in Windows TEMP.
The check may take a few minutes. It installs and restores art, frames, native audio and custom WAV
packages on a temporary copy, tests conflict protection, and verifies byte-identical rollback.
The selected client stays unchanged. No game launch, audio playback or actual plugin installation occurs.
Real BepInEx is not required; temporary test markers are used. This does not verify in-game playback,
the MP3 codec or other mods. Reference cards 202103 and 112103 must retain compatible definitions.
Reports go to `%LOCALAPPDATA%/GwentCardEditor/Reports` and contain local paths.
Build each subsequent package against the current client state. Restore related packages in reverse order.

## 简体中文

在“外观”中选择“检查媒体资源包”并确认。需要完整编辑器、已打开且无未保存修改的
GWENT 客户端，以及 Windows 临时目录中至少512 MB可用空间，可能耗时几分钟。
检查仅在临时副本中安装并还原插画、边框、原生音频和自定义 WAV 资源包，
验证冲突保护以及逐字节完整还原。不修改所选客户端，不启动游戏，不播放音频，
不安装真正的插件。检查使用临时测试标记，无需实际安装 BepInEx。
成功结果不能确认游戏内播放、MP3 解码器或与其他模组的兼容性。
参考卡牌202103和112103需要保留兼容定义。
报告保存在 `%LOCALAPPDATA%/GwentCardEditor/Reports`，包含本地路径。
请基于客户端当前状态生成后续资源包，并按相反顺序还原相关资源包。
