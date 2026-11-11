namespace GwentCardEditor;

internal static class UiText
{
    public static string Language { get; set; } = "en-us";
    private static readonly Dictionary<string, Dictionary<string, string>> Extra = new();
    private static Dictionary<string, string> Translation(string code)
    {
        if (Extra.TryGetValue(code, out var result)) return result;
        try { result = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Translations", code + ".json"))) ?? new(); }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { result = new(); }
        Extra[code] = result;
        return result;
    }
    public static bool Complete => Language is "ru-ru" or "en-us" or "zh-cn" || Labels.Keys.All(k => Translation(Language).TryGetValue(k, out string? s) && !string.IsNullOrWhiteSpace(s));
    private static readonly Dictionary<string, string[]> Labels = """
Title|GWENT: редактор карт|GWENT Card Editor|昆特牌卡牌编辑器
Meaning.ThisValue|это значение|this value|此数值
Meaning.Formula|формула с этим значением|formula using this value|包含此数值的公式
Meaning.Excerpt|В описании карты:|In the card description:|卡牌描述中：
Meaning.Comparison|Сравнение при проверке карт:|Comparison when testing cards:|检查卡牌时的比较：
Meaning.Action|Далее выбранные карты используются для:|Selected cards are then used for:|所选卡牌随后用于：
Meaning.Template|Создаваемая карта:|Card to spawn:|生成的卡牌：
AbilityHelp.TemplateCopies|Количество новых экземпляров указанной ниже карты за одно выполнение этого шага.|New instances of the card named below per execution of this step.|此步骤每次执行生成的下述卡牌的新实例数量。
AbilityHelp.BaseCopies|Создаются новые карты по шаблону источника. Накопленные усиления, изменения брони и статусы источника этим шагом не переносятся. Последующие шаги могут менять параметры новых карт отдельно.|Creates new cards from the source template. This step does not transfer accumulated boosts, armor changes or statuses. Later steps may modify the new cards separately.|从来源模板生成新卡牌。此步骤不继承来源已累积的增益、护甲变化或状态。后续步骤可单独修改新卡牌。
Meaning.CopySource.Self|Источник шаблона: сама эта карта.|Template source: this card itself.|模板来源：此卡牌自身。
Meaning.CopySource.ChosenTarget|Источник шаблона: цель, выбранная игроком.|Template source: a target selected by the player.|模板来源：玩家所选目标。
Meaning.CopySource.ChosenCard|Источник шаблона: карта из окна выбора.|Template source: a card from the choice window.|模板来源：选择窗口中的卡牌。
Meaning.CopySource.ChosenOption|Источник шаблона: выбранный вариант из предложенных карт.|Template source: the selected option from the offered cards.|模板来源：所提供卡牌中的选定选项。
Meaning.CopySource.RandomCard|Источник шаблона: случайно выбранная карта.|Template source: a randomly selected card.|模板来源：随机选择的卡牌。
Meaning.CopyPlayer.Owner|Новые карты создаются у владельца способности.|New cards are created for the ability owner.|为技能拥有者生成新卡牌。
Meaning.CopyPlayer.Opponent|Новые карты создаются у противника владельца способности.|New cards are created for the ability owner's opponent.|为技能拥有者的对手生成新卡牌。
Meaning.CopyPlace.Melee|Зона создания: ближний ряд.|Spawn zone: melee row.|生成区域：近战排。
Meaning.CopyPlace.Ranged|Зона создания: дальний ряд.|Spawn zone: ranged row.|生成区域：远程排。
Meaning.CopyPlace.Hand|Зона создания: рука.|Spawn zone: hand.|生成区域：手牌。
Meaning.CopyPlace.Deck|Зона создания: колода.|Spawn zone: deck.|生成区域：牌组。
Meaning.CopyPlace.Graveyard|Зона создания: сброс.|Spawn zone: graveyard.|生成区域：墓场。
Meaning.CopyPlace.PlayStack|Зона создания: служебная область разыгрывания, не рука и не ряд.|Spawn zone: the play stack, not the hand or a board row.|生成区域：打出区域，不是手牌或战场排。
Meaning.CopyPlace.SelfZone|Зона такого же типа, как у этой карты; для ряда совпадает его тип (ближний или дальний). Сторона игрока указана отдельно.|Same zone type as this card; for a row, the same row type (melee or ranged). The player side is shown separately.|与此卡牌相同的区域类型；若为排，则近战或远程类型相同。玩家方另行标示。
Meaning.CopyPlace.ChosenCardZone|Зона такого же типа, как у выбранной карты. Сторона игрока указана отдельно.|Same zone type as the selected card. The player side is shown separately.|与所选卡牌相同的区域类型。玩家方另行标示。
Meaning.CopyPlace.ChosenRow|Тип ряда определяется выбором ряда. Сторона игрока указана отдельно.|Row type is determined by the row selection. The player side is shown separately.|排类型由排的选择决定。玩家方另行标示。
Meaning.CopyPlace.RandomAvailableRow|Место создания: конец случайного ряда, в котором есть свободное место.|Spawn position: the end of a random row with available space.|生成位置：有空位的随机排末端。
Meaning.CopyDeckPosition.Top|Позиция в колоде: сверху.|Deck position: top.|牌组位置：顶部。
Meaning.CopyDeckPosition.Bottom|Позиция в колоде: снизу.|Deck position: bottom.|牌组位置：底部。
Meaning.CopyRelativePlace.RightOfSelf|Место создания: справа от этой карты.|Spawn position: to the right of this card.|生成位置：此卡牌右侧。
Meaning.CopyRelativePlace.LeftOfSelf|Место создания: слева от этой карты.|Spawn position: to the left of this card.|生成位置：此卡牌左侧。
Meaning.CopyRelativePlace.RightOfChosenTarget|Место создания: справа от выбранной цели.|Spawn position: to the right of the selected target.|生成位置：所选目标右侧。
Meaning.CopyRelativePlace.LeftOfChosenTarget|Место создания: слева от выбранной цели.|Spawn position: to the left of the selected target.|生成位置：所选目标左侧。
Meaning.CopyRelativePlace.RightOfChosenCard|Место создания: справа от выбранной карты.|Spawn position: to the right of the selected card.|生成位置：所选卡牌右侧。
Meaning.CopyRelativePlace.LeftOfChosenCard|Место создания: слева от выбранной карты.|Spawn position: to the left of the selected card.|生成位置：所选卡牌左侧。
Meaning.CopyScope.MultipleSteps|Значение используется в нескольких шагах создания и меняется для всех этих шагов, когда они выполняются.|This value is shared by several creation steps and changes all of them whenever they execute.|多个生成步骤共用此数值，修改将作用于每个执行的相关步骤。
Meaning.UnknownTemplate|название отсутствует в данных клиента|name missing from client data|客户端数据中缺少名称
Meaning.Base|Исходная часть вычисления; другие прибавки и множители сохраняются.|Starting part of the calculation; other additions and multipliers remain.|计算的初始部分，其他加值和倍率保持不变。
Meaning.Alternative|Значение отдельной альтернативной ветки, а не прибавка к основному значению.|Value of a separate alternative branch, not an addition to the normal value.|独立替代分支的数值，不是对通常数值的追加。
Meaning.PowerType.Current|Сравнивается текущая сила карты.|Compares the card's current power.|比较卡牌的当前战力。
Meaning.PowerType.Base|Сравнивается изначальная сила карты.|Compares the card's base power.|比较卡牌的基础战力。
Meaning.PowerType.Armor|Сравнивается броня карты.|Compares the card's armor.|比较卡牌的护甲。
Meaning.Operation.Add|Операция: добавить указанное количество.|Operation: add the specified amount.|操作：增加指定数量。
Meaning.Operation.Remove|Операция: убрать указанное количество.|Operation: remove the specified amount.|操作：移除指定数量。
Meaning.Operation.Set|Операция: установить указанное количество, не прибавить.|Operation: set to the specified amount, not add.|操作：设为指定数量，而非增加。
Meaning.Target.Self|Получатель эффекта: сама эта карта.|Effect recipient: this card itself.|效果接受者：此卡牌自身。
Meaning.Target.ChosenTarget|Получатель эффекта: выбранная игроком цель или цели по шаблону выбора.|Effect recipient: the player's selected target or selection-shape targets.|效果接受者：玩家所选目标或选择范围内的目标。
Meaning.Target.ChosenCard|Получатель эффекта: карта из окна выбора.|Effect recipient: a card from the choice window.|效果接受者：选择窗口中的卡牌。
Meaning.Target.Random|Получатель эффекта выбирается случайно.|The effect recipient is selected randomly.|随机选择效果接受者。
Meaning.Target.Spawned|Эффект применяется к созданным этим узлом картам.|The effect applies to cards spawned by this step.|效果作用于此步骤生成的卡牌。
Meaning.Action.Destroy|уничтожения|destruction|摧毁
Meaning.Action.Banish|изгнания|banishment|放逐
Meaning.Action.Move|перемещения|moving|移动
Meaning.Action.Play|разыгрывания|playing|打出
Meaning.Action.Status|изменения статуса|status modification|修改状态
Meaning.Action.StatusDuration|изменения длительности статуса|status-duration modification|修改状态持续时间
Meaning.Action.Transform|превращения|transformation|变形
Meaning.Action.Reset|восстановления исходных параметров|resetting card values|重置卡牌数值
Meaning.Action.Timer|изменения таймера|timer modification|修改计时器
Meaning.Action.OrderReadiness|изменения готовности Приказа|Order-readiness modification|修改指令可用状态
Meaning.Action.Spawn|создания карты|spawning a card|生成卡牌
Meaning.Action.Draw|добора|drawing|抽牌
Meaning.Selection.Random|Карты для этого шага выбираются случайно.|Cards for this step are selected randomly.|此步骤随机选择卡牌。
Meaning.Selection.Ordered|В этом шаге отключён случайный выбор карт.|Random card selection is disabled for this step.|此步骤不使用随机卡牌选择。
Meaning.Player.Owner|Игрок, для которого выполняется шаг: владелец способности.|Player for this step: the ability owner.|此步骤的玩家：技能拥有者。
Meaning.Player.Opponent|Игрок, для которого выполняется шаг: противник владельца способности.|Player for this step: the ability owner's opponent.|此步骤的玩家：技能拥有者的对手。
MeaningHelp.Damage|Урон за одно выполнение этого эффекта. Число целей и повторов задаётся отдельно.|Damage per execution of this effect. Target and repeat counts are separate.|此效果每次执行的伤害。目标数和重复次数另行设置。
MeaningHelp.Boost|Усиление за одно выполнение этого эффекта. Это не изменение изначальной силы.|Boost per execution of this effect. Does not change base power.|此效果每次执行的增益，不改变基础战力。
MeaningHelp.Heal|Предел исцеления за одно выполнение. Большие исходные числа могут обозначать полное исцеление.|Healing limit per execution. Large original values may implement full healing.|每次执行的治疗上限。较大的原始值可能用于完全治疗。
MeaningHelp.Strengthen|На сколько увеличить изначальную силу за одно выполнение эффекта.|How much base power to add per execution.|每次执行增加的基础战力。
MeaningHelp.BaseLoss|На сколько уменьшить изначальную силу за одно выполнение эффекта.|How much base power to remove per execution.|每次执行降低的基础战力。
MeaningHelp.SetPower|Установить текущую силу равной этому значению, а не усилить на него.|Set current power to this value, rather than boost by it.|将当前战力设为此数值，而非增加此数值。
MeaningHelp.SetBasePower|Установить изначальную силу равной этому значению.|Set base power to this value.|将基础战力设为此数值。
MeaningHelp.ArmorGain|Сколько брони добавить за одно выполнение эффекта.|Armor to add per execution.|每次执行增加的护甲。
MeaningHelp.ArmorLoss|Сколько брони убрать за одно выполнение эффекта.|Armor to remove per execution.|每次执行移除的护甲。
MeaningHelp.SetArmor|Установить броню равной этому значению, а не добавить её.|Set armor to this value, rather than add armor.|将护甲设为此数值，而非增加护甲。
MeaningHelp.ArmorChange|Количество брони для операции, указанной ниже.|Armor amount for the operation shown below.|下述操作使用的护甲数量。
MeaningHelp.SelectionMin|Минимум карт, которые нужно выбрать в этом запросе. Это не число всех предложенных вариантов и не число повторений эффекта.|Minimum cards to select in this request. Not the number of offered choices or effect repetitions.|本次请求至少选择的卡牌数。不是候选卡牌数量或效果重复次数。
MeaningHelp.SelectionMax|Максимум карт, которые можно выбрать в этом запросе. Это не число всех предложенных вариантов и не число повторений эффекта.|Maximum cards selectable in this request. Not the number of offered choices or effect repetitions.|本次请求最多选择的卡牌数。不是候选卡牌数量或效果重复次数。
MeaningHelp.SelectionCount|Количество выбираемых карт в этом шаге. Дальнейшее действие и область эффекта определяются способностью.|Cards selected in this step. The ability determines the subsequent action and effect area.|此步骤选择的卡牌数。后续操作和效果范围由技能决定。
MeaningHelp.Spawn|Количество карт, создаваемых за одно выполнение именно этого шага.|Cards spawned per execution of this specific step.|此特定步骤每次执行生成的卡牌数。
MeaningHelp.Copy|Количество копий, создаваемых за одно выполнение этого шага. Не число призываемых карт из существующей колоды.|Copies created per execution of this step. Not the number of existing cards summoned from the deck.|此步骤每次执行生成的复制数量，不是从牌组召唤现有卡牌的数量。
MeaningHelp.Draw|Сколько карт взять за одно выполнение этого шага.|Cards to draw per execution of this step.|此步骤每次执行抽取的卡牌数。
MeaningHelp.Reveal|Сколько карт вскрыть за одно выполнение этого шага. Не длительность видимости.|Cards to reveal per execution of this step. Not reveal duration.|此步骤每次执行揭示的卡牌数，不是揭示持续时间。
MeaningHelp.Profit|Монеты от Выгоды; другие способы получения монет этой картой задаются отдельно.|Coins from Profit; other coin gains on this card are configured separately.|利润提供的金币；此卡牌的其他金币收益另行设置。
MeaningHelp.Coins|Монеты от этого отдельного эффекта, не обязательно Выгода.|Coins from this specific effect, not necessarily Profit.|此特定效果提供的金币，不一定是利润。
MeaningHelp.Fee|Стоимость одного применения Платежа. Не величина его эффекта.|Cost of one Fee activation. Not the effect's amount.|一次费用技能的花费，不是效果数值。
MeaningHelp.Tribute|Стоимость Дани. Не величина получаемого за неё эффекта.|Tribute cost. Not the amount of the resulting effect.|献金的花费，不是所获效果的数值。
MeaningHelp.Bleeding|Длительность накладываемого Кровотечения в ходах, не урон за один ход.|Duration of applied Bleeding in turns, not damage per turn.|施加的重伤持续回合数，不是每回合伤害。
MeaningHelp.Vitality|Длительность накладываемой Выносливости в ходах, не усиление за один ход.|Duration of applied Vitality in turns, not boost per turn.|施加的活力持续回合数，不是每回合增益。
MeaningHelp.InitialCharges|Число начальных применений существующего Приказа. Пополнение зарядов задаётся отдельно.|Initial uses of the existing Order. Charge replenishment is separate.|已有指令的初始使用次数。充能补充另行设置。
MeaningHelp.ChargeRefill|Сколько зарядов добавляет одно срабатывание условия пополнения.|Charges added by one activation of the refill condition.|补充条件每次触发增加的充能。
MeaningHelp.ChargeChange|Количество зарядов для указанной ниже операции. Не начальные заряды карты.|Charges for the operation shown below. Not the card's initial charges.|下述操作使用的充能数量，不是卡牌的初始充能。
MeaningHelp.InitialTimer|Начальное число отсчёта до срабатывания способности. Не перезарядка Приказа.|Initial countdown until the ability triggers. Not an Order cooldown.|技能触发前的初始倒计时，不是指令冷却。
MeaningHelp.Cooldown|Число ходов до повторной готовности существующего Приказа.|Turns until the existing Order becomes ready again.|已有指令再次可用所需的回合数。
MeaningHelp.CooldownReduction|На сколько уменьшить оставшееся время восстановления цели.|How much to reduce the target's remaining cooldown.|减少目标剩余冷却的数值。
MeaningHelp.Adrenaline|Способность Адреналина активна при этом количестве карт в руке или меньшем. Показано игровое значение, без внутреннего смещения.|Adrenaline is active with this many cards in hand or fewer. Shows the in-game value without the internal offset.|手牌数不高于此数值时激活亢奋。显示游戏数值，不含内部偏移。
MeaningHelp.Berserk|Порог текущей силы для условия Берсерка: это значение или меньше.|Current-power threshold for Berserk: this value or less.|狂暴的当前战力阈值：不高于此数值。
MeaningHelp.Bloodthirst|Сколько вражеских отрядов должно быть повреждено для условия Жажды крови.|Enemy units that must be damaged to satisfy Bloodthirst.|满足战狂条件所需的受伤敌方单位数量。
MeaningHelp.PowerFilter|Граница силы при проверке или отборе карт. Дальнейшая обработка зависит от способности. Это не урон или усиление.|Power boundary when testing or filtering cards. The ability determines subsequent handling. Not damage or boost.|检查或筛选卡牌时的战力界限。后续处理由技能决定，不是伤害或增益。
MeaningHelp.ProvisionFilter|Граница стоимости найма при проверке списка карт. Саму стоимость карт не меняет.|Provision boundary when testing a card list. Does not change the cards' provision costs.|检查卡牌列表时的人口界限，不改变卡牌的人口费用。
MeaningHelp.ChargesFilter|Количество зарядов, с которым сравнивается цель при проверке условия. Заряды не добавляются.|Charge count used to test a condition on a card. Does not add charges.|用于检查卡牌条件的充能数量，不会增加充能。
MeaningHelp.PowerRanking|Отбор по месту в порядке силы, не сравнение силы с числом.|Selection by power ranking, not comparison against a numeric power threshold.|按战力排名筛选，不是与数值战力阈值比较。
MeaningHelp.Rows|Количество рядов для выбора игроком, не отрядов в ряду.|Rows the player selects, not units on a row.|玩家选择的排数，不是排中的单位数。
BackupLocationError|Не удалось создать папку бэкапов рядом с редактором. Выберите доступную для записи папку.|Cannot create the backup folder beside the editor. Choose a writable folder.|无法在编辑器旁创建备份文件夹。请选择可写入的文件夹。
BackupChooseFolder|Папка для бэкапов|Backup folder|备份文件夹
BackupPathLong|Путь слишком длинный. Выберите папку ближе к корню диска.|The path is too long. Choose a folder closer to the drive root.|路径过长。请选择更接近磁盘根目录的文件夹。
BackupClient|Клиент:|Client:|客户端：
BackupRestoreHint|Не удаляйте этот пакет: он содержит файлы для отката. Восстанавливайте пакеты в обратном порядке применения.|Keep this package: it contains rollback files. Restore packages in reverse installation order.|请保留此资源包，其中包含回滚文件。请按安装顺序的相反顺序恢复资源包。
BackupAnimationRemoval|ОтклАним|RemoveAnimation|移除动画
BackupAnimated|АнимРамки|AnimatedFrames|动画边框
BackupAppearance|Оформление|Appearance|外观
BackupArt|Арт|Art|插画
BackupAudio|Звук|Audio|音频
BackupCustomAudio|СвойЗвук|CustomAudio|自定义音频
BackupCards|Карты|Cards|卡牌
BackupOpen|Открыть папку бэкапов|Open Backup Folder|打开备份文件夹
TranslationPreview|Перевод интерфейса|Interface translation|界面翻译
Ability|Способность|Ability|技能
Stratagem|Стратегический приём|Stratagem|战术
Art|Арт|Art|插画
Audio|Звук|Audio|音频
AudioChoose|Звук другой карты…|Use Another Card's Audio…|使用其他卡牌的音频…
AudioScope|Штатный набор можно взять у другой карты этого клиента без BepInEx. Свой WAV или MP3 можно назначить только премиальному просмотру через отдельный BepInEx-плагин. Обе функции экспериментальные.|Reuse another card's native audio without BepInEx. A custom WAV or MP3 can replace premium-preview audio through a separate BepInEx plugin. Both features are experimental.|可直接使用此客户端中其他卡牌的原生音频，无需 BepInEx。自定义 WAV 或 MP3 仅能通过独立 BepInEx 插件替换闪卡预览音频。两项功能均为实验功能。
AudioNativeScope|Перенос всего штатного звукового набора другой карты: реплики, эффекты и звук премиальной версии. Только из этого же клиента, без BepInEx. Экспериментальная функция.|Reuse another card's complete native audio set: voices, effects and premium audio. Same client only, no BepInEx required. Experimental.|使用此客户端中另一张卡牌的完整原生音频：语音、音效和闪卡音频。不需要 BepInEx。实验功能。
AudioNativeRestore|Восстановить штатный набор из пакета…|Restore Native Audio Package…|恢复原生音频资源包…
CustomAudio|Свой звук премиальной карты|Custom Premium Audio|自定义闪卡音频
CustomAudioChoose|Своё аудио для премиального просмотра…|Custom Premium Audio…|自定义闪卡音频…
CustomAudioRestore|Отменить своё аудио из пакета…|Restore Custom Audio Package…|撤销自定义音频资源包…
CustomAudioScope|Только звук премиального просмотра выбранной карты. Нужен уже установленный BepInEx IL2CPP; загрузчик автоматически не устанавливается. Экспериментальная функция.|Selected card's premium-preview audio only. Requires an existing BepInEx IL2CPP installation; the loader is not installed automatically. Experimental feature.|仅替换所选卡牌的闪卡预览音频。需要已安装 BepInEx IL2CPP，不会自动安装加载器。实验功能。
CustomAudioDuration|Длительность, с:|Duration, seconds:|时长（秒）：
CustomAudioStart|Начало, с|Start, s|开始（秒）
CustomAudioSelectRange|Выбор аудиофрагмента|Select Audio Range|选择音频片段
CustomAudioRangeLimit|Выберите фрагмент длительностью не более 120 секунд. Исходная запись не изменяется.|Select a clip of up to 120 seconds. The source recording stays unchanged.|请选择不超过120秒的片段。原始录音保持不变。
CustomAudioExtract|Подготовить фрагмент|Prepare Clip|准备片段
CustomAudioEnd|Конец, с|End, s|结束（秒）
CustomAudioFadeIn|Плавное начало, с|Fade in, s|淡入（秒）
CustomAudioFadeOut|Затухание, с|Fade out, s|淡出（秒）
CustomAudioRange|Выберите непустой фрагмент. Сумма плавного начала и затухания не должна превышать его длительность.|Choose a non-empty range. Total fade duration must not exceed the selected clip duration.|请选择非空片段。淡入和淡出总时长不能超过片段时长。
CustomAudioPlay|Прослушать фрагмент|Play Clip|播放片段
CustomAudioStop|Стоп|Stop|停止
CustomAudioGain|Громкость в игре:|In-game Volume:|游戏内音量：
CustomAudioGainHint|Множитель громкости эффектов GWENT. Предпросмотр воспроизводит исходный файл с громкостью Windows, без этого множителя.|Scales GWENT's effect volume. File preview uses Windows volume without this multiplier.|调整昆特牌音效音量。文件预览使用 Windows 音量，不应用此倍率。
CustomAudioLoop|Повторять до закрытия просмотра|Loop Until Preview Closes|循环播放至关闭预览
CustomAudioFormat|Для WAV нужен PCM 16 бит: моно или стерео, 44,1 или 48 кГц, до 50 МБ. Готовый фрагмент не длиннее 120 секунд. MP3 подготавливается отдельным декодером.|WAV requires 16-bit PCM: mono or stereo, 44.1 or 48 kHz, up to 50 MB. Prepared clips must not exceed 120 seconds. MP3 uses a separate decoder.|WAV 需为16位 PCM，单声道或立体声，44.1或48 kHz，不超过50 MB。处理后的片段不得超过120秒。MP3 使用独立解码器。
CustomAudioClient|Откройте папку полноценного клиента GWENT для пакета своего звука.|Open a complete GWENT client folder for custom audio packages.|请打开完整的昆特牌客户端文件夹以使用自定义音频资源包。
CustomAudioLoader|Для установки нужен уже установленный BepInEx IL2CPP. Пакет можно сохранить, но установка в этот клиент отключена.|Installation requires an existing BepInEx IL2CPP setup. You can save a package, but installation into this client is disabled.|安装需要已配置 BepInEx IL2CPP。可以保存资源包，但无法安装到此客户端。
CustomAudioConflict|Пакет, настройки или файлы плагина изменились. Перезапись отменена. Отменяйте более поздние пакеты в обратном порядке.|Package, configuration or plugin files have changed. Overwrite cancelled. Restore later packages in reverse order.|资源包、配置或插件文件已更改，已取消覆盖。请按相反顺序撤销后续资源包。
CustomAudioInstallConfirm|Закройте GWENT. Установить отдельный экспериментальный плагин и WAV для этой карты? Другие моды замены её звука необходимо отключить вручную. Родные файлы игры не меняются.|Close GWENT. Install the separate experimental plugin and this card's WAV? Disable other audio overrides for this card manually. Native game files stay unchanged.|请关闭昆特牌。是否安装独立实验插件及此卡牌的 WAV？请手动禁用此卡牌的其他音频替换模组。不修改原生游戏文件。
CustomAudioInstalled|Плагин и WAV установлены. Настройка загрузится при следующем запуске GWENT.|Plugin and WAV installed. Settings load on the next GWENT launch.|插件和 WAV 已安装，将在下次启动昆特牌时加载设置。
CustomAudioRestoreConfirm|Закройте GWENT. Вернуть прежние настройки и файлы плагина из этого пакета? Чужие изменения не перезаписываются.|Close GWENT. Restore the previous plugin settings and files from this package? Foreign edits will not be overwritten.|请关闭昆特牌。是否从此资源包恢复先前插件设置和文件？不会覆盖其他修改。
CustomAudioRestored|Предыдущие файлы и настройки своего звука восстановлены.|Previous custom-audio files and settings restored.|已恢复先前自定义音频文件和设置。
CustomMp3Missing|В сборке отсутствует компонент импорта MP3. Нужна полная версия редактора с папкой AudioDecode.|The MP3 import component is missing. Use the complete editor with its AudioDecode folder.|缺少 MP3 导入组件。请使用包含 AudioDecode 文件夹的完整编辑器。
CustomMp3Codec|Windows не удалось открыть кодек MP3. Используйте WAV или проверьте наличие мультимедийных компонентов Windows.|Windows could not open its MP3 codec. Use WAV or check that Windows media components are installed.|Windows 无法打开 MP3 解码器。请使用 WAV，或检查 Windows 媒体组件是否已安装。
CustomMp3Limit|MP3 должен быть не больше 50 МБ и 30 минут. Готовый фрагмент — не длиннее 120 секунд.|MP3 limit: 50 MB and 30 minutes. The prepared clip must not exceed 120 seconds.|MP3 不得超过50 MB和30分钟。处理后的片段不得超过120秒。
CustomMp3Output|Выходной файл уже существует или совпадает с исходным. Перезапись отменена.|The output exists or matches the input. Overwrite cancelled.|输出文件已存在或与输入相同。已取消覆盖。
CustomMp3Failed|Не удалось подготовить MP3. Исходный файл не изменён. Подробности в art-worker.log в папке настроек редактора.|Could not prepare the MP3. The source is unchanged. See art-worker.log in the editor settings folder.|无法处理 MP3，原始文件未更改。详情请查看编辑器设置文件夹中的 art-worker.log。
AudioDonor|Карта-источник звука|Audio Source Card|音频来源卡牌
AudioWorking|Сборка пакета звука…|Building audio package…|正在生成音频资源包…
AudioInstallConfirm|Закройте GWENT. Заменить звуковой набор выбранной карты? Изменится data_definitions; пакет содержит резервную копию всего архива. Внешний вид и способности не меняются.|Close GWENT. Replace the selected card's audio set? This changes data_definitions; the package contains a complete archive backup. Appearance and abilities stay unchanged.|请关闭昆特牌。是否替换所选卡牌的音频？这会修改 data_definitions，资源包含完整存档备份。外观和技能保持不变。
AudioInstalled|Звук переназначен. Проверьте карту в игре. После закрытия окна данные клиента будут перечитаны.|Audio reassigned. Check the card in game. Client data will reload when this window closes.|音频已重新指定。请在游戏中检查卡牌。关闭此窗口后将重新加载客户端数据。
AudioRestoreConfirm|Закройте GWENT. Восстановить архив из пакета звука? Последующие изменения не перезаписываются: другие пакеты нужно отменять в обратном порядке.|Close GWENT. Restore the archive from the audio package? Later changes will not be overwritten: restore other packages in reverse order.|请关闭昆特牌。是否从音频资源包恢复存档？不会覆盖后续修改，请按相反顺序恢复其他资源包。
AudioRestored|Предыдущий звуковой набор восстановлен.|Previous audio set restored.|已恢复先前音频。
AudioUnsupported|Для этой карты или структуры клиента перенос звука не подтверждён. Изменения заблокированы.|Audio reassignment is not verified for this card or client structure. Changes are blocked.|尚未验证此卡牌或客户端结构的音频替换。修改已被阻止。
AudioSameSource|Эти карты уже используют один звуковой набор. Выберите другую карту.|These cards already share one audio set. Choose another card.|这些卡牌已共用相同音频，请选择其他卡牌。
AudioWrongPackage|Выберите пакет переноса звука. Арты и общее оформление восстанавливаются в своих вкладках.|Choose an audio-reassignment package. Restore art and global appearance from their own tabs.|请选择音频替换资源包。请在对应选项卡恢复插画和全局外观。
GlobalVisuals|Оформление|Appearance|卡牌外观
VisualConfigure|Общее оформление карт…|Global Card Appearance…|全局卡牌外观…
VisualScope|Общие настройки золотых и бронзовых карт. Экспериментальная функция: требуется проверка в игре. Характеристики, числа и статусы не меняются.|Global settings for gold and bronze cards. Experimental: verify in game. Stats, numbers and statuses stay unchanged.|金卡和铜卡的全局设置。实验功能，需在游戏中验证。不改变属性、数字和状态。
VisualFrames|Рамки|Frames|边框
VisualKeep|Не менять|Keep Current|保持当前
VisualNoFrames|Без рамок|No Frames|无边框
VisualRogueFolder|Папка Rogue Mage…|Rogue Mage Folder…|Rogue Mage 文件夹…
VisualBronze|Бронзовая|Bronze|铜框
MediaCheck|Проверить медиапакеты…|Check Media Packages…|检查媒体资源包…
MediaCheckScope|Сквозная проверка арта, рамок, звука и отката на временной копии ресурсов. Выбранный клиент не изменяется. Не заменяет проверку в игре.|Check art, frames, audio and rollback on a temporary resource copy. The selected client stays unchanged. Does not replace in-game testing.|在临时资源副本上检查插画、边框、音频及还原。不会修改所选客户端，不能替代游戏内测试。
MediaCheckConfirm|Запустить проверку на временной копии? Потребуется не менее 512 МБ свободного места и до нескольких минут. Звук не воспроизводится, ничего в игру не устанавливается.|Run the check on a temporary copy? Requires at least 512 MB of free space and may take a few minutes. No audio playback or game installation.|是否在临时副本上运行检查？需要至少512 MB可用空间，可能耗时几分钟。不播放音频，也不安装到游戏。
MediaCheckPassed|Проверка медиапакетов пройдена. Установка и полный откат на временной копии проверены. Отчёт:|Media package check passed. Installation and full rollback verified on a temporary copy. Report:|媒体资源包检查通过，已在临时副本上验证安装及完整还原。报告：
MediaCheckFailed|Проверка не завершена успешно. Совместимость выбранного клиента не подтверждена. Подробности в отчёте:|The check did not complete successfully. Compatibility with the selected client is not confirmed. See the report:|检查未成功完成，尚未确认与所选客户端兼容。详情请查看报告：
MediaTestReport|Отчёт проверки должен быть новым файлом вне папки выбранного клиента.|The report must be a new file outside the selected client folder.|报告必须是所选客户端文件夹之外的新文件。
VisualBronzeFrames|Бронзовые рамки|Bronze Frames|铜卡边框
VisualGoldFrames|Золотые рамки|Gold Frames|金卡边框
VisualFieryFiend|Fiery Fiend (статичная)|Fiery Fiend (Static)|Fiery Fiend（静态）
FieryAnimated|Fiery Fiend (анимированная)|Fiery Fiend (Animated)|Fiery Fiend（动态）
FieryInstallConfirm|Закройте GWENT. Установить оформление и отдельный BepInEx-плагин анимации для всех золотых карт, кроме лидеров? Предыдущие файлы и настройки плагина сохранятся в пакете.|Close GWENT. Install the appearance and a separate BepInEx animation plugin for all gold cards, excluding leaders? Previous plugin files and settings will be backed up in the package.|请关闭昆特牌。是否安装外观及独立 BepInEx 动画插件，作用于所有金卡但不包括领袖？此前插件文件和设置将备份到资源包中。
FieryRemove|Удалить анимацию|Remove Animation|移除动态边框
FieryScope|Анимация всех золотых карт, кроме лидеров. Нужен BepInEx IL2CPP. Предпросмотр здесь статичный. Массовое отображение ещё требует проверки в игре.|Animation for all gold cards, excluding leaders. Requires BepInEx IL2CPP. This preview is static. Multi-card rendering still needs in-game testing.|为所有金卡添加动画，领袖除外。需要 BepInEx IL2CPP。此处预览为静态，多卡显示仍需游戏内测试。
FieryLoader|Для установки анимации нужен уже установленный BepInEx IL2CPP. Пакет можно сохранить, но установить в этот клиент нельзя. Статичный вариант работает без загрузчика.|Animation requires an existing BepInEx IL2CPP installation. You can export the package, but cannot install it in this client. The static version needs no loader.|动画需要已安装的 BepInEx IL2CPP。可以导出资源包，但无法安装到此客户端。静态版本无需加载器。
FieryConflict|Файлы плагина или оформления отличаются от ожидаемого состояния. Чужие изменения не перезаписаны. Сначала проверьте порядок отмены пакетов.|Plugin or appearance files differ from the expected state. Foreign changes were not overwritten. Check package restore order first.|插件或外观文件与预期状态不同，未覆盖其他修改。请先检查资源包还原顺序。
FieryRuntime|Компонент анимированных рамок отсутствует или повреждён. Восстановите полную папку редактора OptionalPlugins/FieryFiend.|Animated-frame runtime is missing or damaged. Restore the complete OptionalPlugins/FieryFiend editor folder.|动态边框组件缺失或损坏。请恢复完整的 OptionalPlugins/FieryFiend 文件夹。
FieryOutput|Выберите новую папку пакета вне выбранного клиента.|Choose a new package folder outside the selected client.|请选择所选客户端之外的新资源包文件夹。
FieryActive|Найден плагин анимированной рамки. Для перехода на статичное оформление сначала удалите анимацию.|An animated-frame plugin is present. Remove animation before switching to static appearance.|已检测到动态边框插件。切换到静态外观前请先移除动画。
FieryRemoveConfirm|Закройте GWENT. Удалить плагин анимации? Текущее статичное оформление останется. Будет создан отдельный пакет с резервной копией файлов плагина.|Close GWENT. Remove the animation plugin? Current static appearance remains. A separate backup package will be created for the plugin files.|请关闭昆特牌。是否移除动画插件？保留当前静态外观，并为插件文件创建独立备份资源包。
FieryRemoved|Плагин анимации удалён. Перезапустите игру. Резервный пакет:|Animation plugin removed. Restart the game. Backup package:|已移除动画插件。请重启游戏。备份资源包：
FieryAlreadyRemoved|Плагин анимации уже удалён. Файлы не менялись.|Animation plugin already removed. No files were changed.|动画插件已移除，未修改任何文件。
VisualGold|Золотая|Gold|金框
VisualFrameAssets|Встроенные рамки отсутствуют или повреждены. Восстановите полную папку редактора Assets/RogueFrames.|Built-in frames are missing or damaged. Restore the editor's complete Assets/RogueFrames folder.|内置卡框缺失或损坏。请恢复编辑器完整的 Assets/RogueFrames 文件夹。
VisualBorder|Вариант рамки Rogue Mage|Rogue Mage Border Variant|Rogue Mage 边框样式
VisualWhiteDragon|Белый дракон|White Dragon|白龙
VisualElite|Элитная|Elite|精英
VisualBanners|Скрыть фракционные знамёна|Hide Faction Banners|隐藏阵营旗帜
VisualRarity|Скрыть значки редкости|Hide Rarity Gems|隐藏稀有度宝石
VisualProvision|Скрыть фон стоимости найма|Hide Provision Backgrounds|隐藏战备值背景
VisualBread|Скрыть значок хлеба и вина|Hide Bread and Wine Icon|隐藏面包与酒图标
VisualWorking|Сборка общего оформления…|Building global appearance…|正在生成全局外观…
VisualInstallConfirm|Закройте GWENT. Применить оформление ко всем золотым и бронзовым картам этого клиента? Пакет содержит резервную копию.|Close GWENT. Apply appearance to all gold and bronze cards in this client? The package contains a backup.|请关闭昆特牌。是否将外观应用到此客户端的全部金卡和铜卡？资源包包含备份。
VisualRestoreConfirm|Закройте GWENT. Вернуть состояние до создания пакета? Если в нём есть плагин анимации, восстановятся и его прежние файлы и настройки. Более поздние пакеты сначала отмените в обратном порядке.|Close GWENT. Restore the state from before this package was created? If it includes animation, previous plugin files and settings will also be restored. Restore later packages in reverse order first.|请关闭昆特牌。是否恢复到创建此资源包之前的状态？若其中包含动画插件，也将恢复其此前文件和设置。请先按相反顺序还原后续资源包。
VisualInstalled|Оформление установлено. Проверьте карты в игре.|Appearance installed. Check cards in game.|外观已安装。请在游戏中检查卡牌。
VisualRestored|Предыдущее оформление восстановлено.|Previous appearance restored.|已恢复先前外观。
VisualAlreadyRestored|Этот пакет уже отменён: файлы совпадают с его резервной копией. Для более раннего оформления нужен более ранний пакет.|This package is already restored: files match its backup. Use an earlier package to restore an earlier appearance.|此资源包已还原：文件与其备份一致。若要恢复更早的外观，请使用更早的资源包。
ArtFileConflict|Файлы ресурсов не совпадают с ожидаемым состоянием этого пакета. Проверьте клиент и порядок отмены пакетов. Ничего не перезаписано.|Resource files do not match the state expected by this package. Check the client and package restore order. Nothing was overwritten.|资源文件与此资源包预期的状态不符。请检查客户端及资源包还原顺序。未覆盖任何文件。
ArtDataConflict|Данные карт отличаются от состояния при создании пакета. Сначала отмените более поздние изменения данных или выберите подходящий пакет. Ничего не перезаписано.|Card data differs from the state used to build this package. Restore later data changes first or select the matching package. Nothing was overwritten.|卡牌数据与生成此资源包时不同。请先还原之后的数据修改或选择匹配的资源包。未覆盖任何文件。
VisualStarterScope|Стартовые колоды: экспериментальный фильтр конструктора и списка выбора колоды. Нужен уже установленный BepInEx IL2CPP. Карты и сохранённые колоды не удаляются.|Starter decks: experimental filter for the deck builder and deck selector. Requires an existing BepInEx IL2CPP installation. No cards or saved decks are deleted.|初始套牌：构筑器和套牌选择列表的实验筛选功能。需要已安装 BepInEx IL2CPP。不删除卡牌或已保存的套牌。
VisualStarterHide|Скрыть стартовые колоды|Hide Starter Decks|隐藏初始套牌
VisualStarterShow|Вернуть стартовые колоды|Show Starter Decks|显示初始套牌
VisualStarterConfirm|Закройте GWENT. Установить экспериментальный плагин скрытия стартовых колод в этот клиент? Другие моды скрытия колод необходимо отключить вручную.|Close GWENT. Install the experimental starter-deck filter into this client? Disable any other deck-hiding mods manually.|请关闭昆特牌。是否在此客户端安装实验性初始套牌筛选插件？请手动禁用其他隐藏套牌的模组。
VisualStarterRestoreConfirm|Закройте GWENT. Удалить только плагин скрытия стартовых колод этого редактора?|Close GWENT. Remove only this editor's starter-deck filter plugin?|请关闭昆特牌。是否仅移除此编辑器的初始套牌筛选插件？
VisualStarterDone|Настройка сохранена. Проверьте списки колод после следующего запуска игры.|Setting saved. Check deck lists after the next game launch.|设置已保存。请在下次启动游戏后检查套牌列表。
VisualPluginUnsupported|Нужен клиент с установленным BepInEx IL2CPP и полный комплект редактора. Загрузчик модов автоматически не устанавливается.|Requires a client with BepInEx IL2CPP and the complete editor package. The mod loader is not installed automatically.|需要已安装 BepInEx IL2CPP 的客户端和完整编辑器。不会自动安装模组加载器。
VisualWrongPackage|Это другой тип пакета. Восстановление артов находится во вкладке «Арт», общего оформления — во вкладке «Оформление».|Wrong package type. Restore card art from the Art tab and global appearance from the Appearance tab.|资源包类型不符。请在插画选项卡恢复插画，在卡牌外观选项卡恢复全局外观。
ArtScope|Только обычная иллюстрация. Характеристики, тексты, звук и премиальные ресурсы не меняются.|Standard illustration only. Stats, text, audio and premium assets stay unchanged.|仅修改普通插画。不改变属性、文本、音频和闪卡资源。
ArtChooseImage|Выбрать изображение…|Choose Image…|选择图片…
ArtRestore|Восстановить из пакета…|Restore from Package…|从资源包恢复…
ArtZoom|Масштаб|Zoom|缩放
ArtResetCrop|Сбросить кадр|Reset Crop|重置裁剪
ArtExport|Сохранить пакет…|Save Package…|保存资源包…
ArtInstall|Установить пакет|Install Package|安装资源包
ArtClose|Закрыть|Close|关闭
ArtWorking|Обработка изображения…|Processing image…|正在处理图片…
ArtExported|Пакет сохранён:|Package saved:|资源包已保存：
ArtInstalled|Арт установлен. Обычная версия карты готова к проверке в игре.|Art installed. The standard card is ready for an in-game check.|插画已安装。可在游戏中检查普通卡牌。
ArtRestored|Исходный арт восстановлен.|Original art restored.|原插画已恢复。
ArtInstallConfirm|Закройте GWENT. Установить обычный арт в этот клиент? Резервная копия находится в сохранённом пакете.|Close GWENT. Install standard art into this client? The saved package contains the backup.|请关闭昆特牌。是否将普通插画安装到此客户端？备份保存在资源包中。
ArtRestoreConfirm|Закройте GWENT. Восстановить файлы арта из выбранного пакета? Изменённые после установки файлы перезаписываться не будут.|Close GWENT. Restore art files from the selected package? Files modified after installation will not be overwritten.|请关闭昆特牌。是否从所选资源包恢复插画？安装后被修改的文件不会被覆盖。
ArtNewFolder|Выберите имя новой, ещё не существующей папки пакета.|Choose a new package folder name that does not exist yet.|请选择尚不存在的新资源包文件夹名称。
ArtNeedsClient|Чтобы изменить иллюстрацию, сначала откройте папку клиента GWENT.|Open a GWENT client folder to edit card art.|请先打开昆特牌客户端文件夹以编辑卡牌插画。
ArtCleanSource|Сначала экспортируйте или отмените правки характеристик и откройте нужный клиент заново. Медиа собираются отдельным пакетом из файлов на диске.|First export or undo stat edits and reopen the desired client. Media uses a separate package built from files on disk.|请先导出或撤销属性修改，再重新打开所需客户端。媒体使用基于磁盘文件的独立资源包。
ArtSeparatePackage|Пакет арта сохраняется отдельно от экспорта характеристик и содержит резервную копию.|The art package is separate from stat exports and contains a backup.|插画资源包与属性导出分开保存，并包含备份。
ArtRuntimeMissing|Не установлен компонент обработки артов. Нужна полная сборка редактора с ArtRuntime.|The art-processing component is missing. Use the complete editor build with ArtRuntime.|缺少插画处理组件。请使用包含 ArtRuntime 的完整编辑器版本。
ArtOperationFailed|Операция с артом не выполнена. Проверьте доступ к файлам и журнал art-worker.log в папке настроек редактора.|Art operation failed. Check file access and art-worker.log in the editor settings folder.|插画操作失败。请检查文件权限及编辑器设置文件夹中的 art-worker.log。
ArtImageTooLarge|Изображение должно быть не больше 25 мегапикселей и 50 МБ, минимум 16 пикселей по каждой стороне.|Image limit: 25 megapixels and 50 MB; at least 16 pixels per side.|图片不得超过2500万像素及50 MB，每边至少16像素。
ArtImageUnsupported|Нужен статичный PNG, JPEG или WebP. Файл повреждён или имеет неподдерживаемый формат.|Use a still PNG, JPEG or WebP. The file is damaged or unsupported.|请使用静态 PNG、JPEG 或 WebP。文件已损坏或格式不受支持。
ArtCropRatio|Неподдерживаемые пропорции подготовленного кадра.|Unsupported prepared crop ratio.|预处理裁剪比例不受支持。
RepairDescription|Согласовать число в описании с усилением|Match description number to boost|使描述数值与增益一致
Ability.both-row-boost|Усиление для обоих размещений|Boost for both Deploy abilities|两种部署技能的增益
SharedBoostDescriptionConflict|В обоих размещениях используется одно число в описании. Изменяйте их общее усиление одинаково.|Both Deploy descriptions use the same number. Set the same boost for both abilities.|两种部署描述使用同一数值。请为两种技能设置相同增益。
TargetCountTextConflict|Не удалось обновить описание для выбранного количества целей. Сохраните исходную формулировку цели или отмените ручную правку текста.|The description could not be updated for this target count. Preserve the existing target wording or undo the manual text edit.|无法按所选目标数量更新描述。请保留原目标措辞，或撤销手动文本修改。
Cardback|Рубашка карты|Card Back|卡背
CardbackChoose|Выбор рубашки|Choose Card Back|选择卡背
CardbackDefault|Стандартная рубашка {0}|Standard card back {0}|标准卡背 {0}
CardbackAll|Применить изображение ко всем 7 рубашкам|Apply image to all 7 supported standard card backs|将图片应用到全部 7 款受支持的标准卡背
CardbackImport|Заменить изображение рубашки|Replace card back image|替换卡背图片
CardbackRestore|Восстановить рубашку|Restore card back|恢复卡背
CardbackScope|Изображение одной выбранной стандартной рубашки или всех семи сразу. Анимации и другие рубашки не меняются.|Replace one selected standard card back or all seven at once. Animations and other card backs remain unchanged.|可替换选中的一款标准卡背，也可一次替换全部七款。动画和其他卡背保持不变。
CardbackUnsupported|Эта рубашка или её структура не поддерживается. Изменения заблокированы.|This card back or its asset structure is unsupported. Changes are blocked.|不支持此卡背或其资源结构。已阻止修改。
CardbackGeometry|Разметка рубашек отличается от проверенной. Изменения заблокированы.|Card back layout differs from the verified version. Changes are blocked.|卡背布局与已验证版本不同。已阻止修改。
CardbackCatalogMismatch|Файлы рубашки не соответствуют каталогу ресурсов клиента. Изменения заблокированы.|Card back files do not match the client asset catalog. Changes are blocked.|卡背文件与客户端资源目录不匹配。已阻止修改。
CardbackCropRatio|Подготовленное изображение рубашки должно иметь пропорции 2:3.|The prepared card back image must have a 2:3 aspect ratio.|预处理的卡背图片必须为2:3比例。
ArtTextureLayout|Схема текстуры этого клиента пока не поддерживается.|This client's texture layout is not supported yet.|尚不支持此客户端的纹理布局。
ArtShared|Этот арт используют несколько карт. Изолированная замена пока заблокирована.|Several cards share this art. Isolated replacement is not supported yet.|多张卡牌共用此插画。暂不支持单独替换。
ArtGameRunning|Закройте GWENT перед установкой или восстановлением арта.|Close GWENT before installing or restoring art.|安装或恢复插画前请关闭昆特牌。
ArtConflict|Файлы клиента или данные карт изменились. Перезапись отменена для сохранения последующих правок. Не удаляйте исходный пакет.|Client files or card data have changed. Overwrite was cancelled to preserve later edits. Keep the original package.|客户端文件或卡牌数据已更改。已取消覆盖以保留后续修改。请保留原资源包。
InitialValue|Начальное значение|Initial value|初始值
InitialStatuses|Исходные статусы|Initial statuses|初始状态
InitialStatusDescription|Названия выбранных статусов добавляются в описание карты|Selected status names are added to the card description|所选状态名称会添加到卡牌描述
AdditionalAbilities|Дополнительные способности|Additional abilities|附加技能
CopiesWithoutCondition|Копии без Взаимосвязи|Copies without Bonded|未触发会师时的复制数量
AbilityHelp.UnbondedCopies|Количество копий в обычной ветви размещения, без срабатывания Взаимосвязи. Не меняет количество копий в ветви Взаимосвязи.|Copies in the regular Deploy branch, without Bonded. Does not change the Bonded branch's copy count.|未触发会师时部署分支的复制数量。不改变会师分支的复制数量。
AbilityHelp.BondedCopies|Количество копий при срабатывании Взаимосвязи. Не меняет количество копий в обычной ветви.|Copies when Bonded is active. Does not change the regular branch's copy count.|触发会师时的复制数量。不改变普通分支的复制数量。
LeaderEntityReplacement|Это служебное пересоздание лидера, а не создание обычных карт.|This recreates the leader entity, not ordinary cards.|这是重建领袖实体，并非生成普通卡牌。
LeaderResetSignal|Это служебный сигнал восстановления лидера, а не усиление.|This is a leader-reset signal, not a boost.|这是恢复领袖的内部信号，并非增益。
GigaChoiceIsolation|Независимые варианты Отвара из гигаскорпиона|Independent Gigascorpion Decoction choices|巨蝎煎药的独立选项
GigaCondenseDamage|Урон за удар (6 ударов)|Damage per strike (6 strikes)|每次伤害（共6次）
GigaSpreadTotal|Общий случайный урон|Total random damage|随机伤害总量
BranStartVeteran|Отряды в начале игры|Units at game start|游戏开始时的单位
WithoutTributeTargets|Количество целей без Дани|Targets without Tribute|未支付贡品的目标数量
FirstBanishChoices|Первое удаление из колоды: количество карт|First banish from deck: card count|首次从牌库放逐：卡牌数量
ExtraBronzeBanishChoices|Дополнительное удаление бронзовых карт|Additional bronze cards to banish|额外放逐的铜色牌数量
SingleTargetDamage|Урон выбранному отряду|Damage to the selected unit|对所选单位的伤害
AllEnemyDamage|Урон каждому вражескому отряду|Damage to each enemy unit|对每个敌军单位的伤害
AbilityHelp.GigaCondense|Урон каждого из шести ударов по выбранному отряду. Количество ударов остаётся равным 6; случайный урон другого варианта не меняется.|Damage of each of six strikes against the selected unit. Strike count stays at 6; the other choice's random damage is unchanged.|对所选单位造成六次伤害，每次的伤害量。次数保持为6；不改变另一选项的随机伤害。
AbilityHelp.GigaSpread|Общий урон, случайно распределяемый по вражескому ряду попаданиями по 1. Не меняет урон или число ударов варианта «Концентрация».|Total damage randomly split across an enemy row in 1-damage hits. Does not change the focused choice's damage or strike count.|以每次1点伤害随机分配到敌方一排的伤害总量。不改变集中选项的伤害或次数。
AbilityHelp.VeteranStrengthen|Увеличение изначальной силы владельца при срабатывании Ветерана. Условия срабатывания сохраняются.|Owner's base-power gain when Veteran triggers. Existing trigger conditions are preserved.|老兵触发时拥有者增加的基础战力。保留原有触发条件。
AbilityHelp.BranStartVeteran|Увеличение изначальной силы отрядов с Ветераном в начале игры. Это не собственный Ветеран Брана между раундами и не усиление при размещении.|Base-power gain for Veteran units at game start. Not Bran's own between-round Veteran or his Deploy boost.|游戏开始时老兵单位增加的基础战力。不是布兰自身在小局之间的老兵，也不是部署增益。
AdditionalAbilityUnsupported|Добавление этой способности для выбранной карты недоступно.|This ability cannot be added to the selected card.|无法为所选卡牌添加此技能。
InitialStatusesUnsupported|Независимость исходных статусов от графа не подтверждена|Initial statuses are not verified as independent of the graph|尚未验证初始状态与技能图相互独立
OpenOfficialGog|Открыть официальный GOG-клиент|Open official GOG client|打开GOG官方客户端
OfficialGogSource|Официальная установка GOG|Official GOG installation|GOG官方安装
CustomClientSource|Выбранная установка клиента|Selected client installation|所选客户端安装
OfficialGogNotFound|Папка официального GOG-клиента не найдена|Official GOG client folder was not found|未找到GOG官方客户端文件夹
StatusReportHash|SHA-256 архива исходных данных|Source data archive SHA-256|源数据归档 SHA-256
StatusBlockFlag|Зависимость от бита статуса|Depends on status bit|依赖状态位
StatusBlockNode|Неподдерживаемый узел графа|Unsupported graph node|不支持的技能图节点
StatusBlockExternal|Внешняя зависимость графа|External graph dependency|技能图外部依赖
StatusBlockGraph|Ошибка проверки графа|Graph validation error|技能图验证错误
StatusBlockTemplate|Поле шаблона|Template field|模板字段
StatusBlockOwnership|Общая или отсутствующая способность|Shared or missing ability|技能共用或缺失
StatusBlockType|Категория карты|Card eligibility|卡牌资格
StatusBlockOther|Дополнительная причина|Additional reason|其他原因
PendingReviewTitle|Проверка изменений|Review changes|检查更改
PendingReviewInstructions|Изменения разных карт собираются в один черновик проекта. Выберите карту, чтобы вернуться к её редактированию, или удалите все её изменения.|Edits from different cards are collected in one project draft. Select a card to continue editing it, or remove all its changes.|不同卡牌的编辑会汇总到项目草稿中。选择卡牌可继续编辑，也可删除该卡的全部更改。
PendingReviewCard|Карта|Card|卡牌
PendingReviewChanges|Изменения|Changes|更改
PendingReviewEditCard|Редактировать карту|Edit card|编辑卡牌
PendingReviewRemoveCard|Удалить изменения карты|Remove card changes|删除卡牌更改
PendingReviewApply|Применить изменения|Apply changes|应用更改
PendingReviewResetAll|Сбросить все изменения|Reset all changes|重置所有更改
PendingReviewCancel|Продолжить редактирование|Continue editing|继续编辑
PendingReviewRemoveConfirm|Удалить все изменения карты «{card}»?|Remove all changes for “{card}”?|删除“{card}”的全部更改？
PendingReviewFieldCount|Полей: {count}|{count} fields|{count} 个字段
PendingReviewSummary|Карт: {cards}; изменений: {changes}|Cards: {cards}; changes: {changes}|卡牌：{cards}；更改：{changes}
PendingReviewRemoveHistory|Удалить изменения карты из черновика|Remove card changes from draft|从草稿中删除卡牌更改
PendingReviewLinkedChanges|Связанные изменения возникли из правки другой карты; удалите карту-источник|These linked changes came from another card edit; remove the source card|这些关联更改由编辑其他卡牌产生；请删除源卡牌
StatusNeedsUnit|Только обычные отряды; лидеры и токены требуют отдельной проверки|Regular units only; leaders and tokens need separate verification|仅限普通单位；领袖和衍生物需要单独验证
StatusNeedsUnitOrArtifact|Только обычные отряды и артефакты; лидеры и токены требуют отдельной проверки|Regular units and artifacts only; leaders and tokens need separate verification|仅限普通单位和神器；领袖和衍生物需要单独验证
Yes|Да|Yes|是
No|Нет|No|否
Refill|Пополнение|Refill|补充量
Reduction|Сокращение|Reduction|减少量
InitialChargesUnsupported|Не подтверждён простой приказ с фиксированными начальными зарядами|A simple Order with fixed initial charges is not verified|尚未验证具有固定初始充能的简单指令
ChargeTextMismatch|Число зарядов в шаблоне и переменная описания не согласованы|Template charges and the description variable are not synchronized|模板充能与描述变量不一致
LocalBalanceNote|Сила и найм из локальных файлов. Поправки Совета баланса не применяются.|Power and provisions come from local files. Balance Council adjustments are not applied.|战力和人口来自本地文件。不应用平衡议会调整。
BalanceCouncil|Совет Баланса|Balance Council|平衡议会
UpdateBalance|Обновить баланс|Update Balance|更新平衡数据
OpenBalanceSnapshot|Открыть снимок|Open Snapshot|打开快照
SaveBalanceSnapshot|Сохранить снимок|Save Snapshot|保存快照
SelectBalanceReference|Исходная база|Original Base|原始数据库
BalanceLocal|Локальные значения|Local values|本地数值
BalanceReady|playgwent.com|playgwent.com|playgwent.com
BalanceDownloading|Загрузка с playgwent.com...|Downloading from playgwent.com...|正在从 playgwent.com 下载...
BalanceSnapshotMissing|Снимка нет: сила и найм заблокированы|No snapshot: power and provisions are locked|无快照：战力和人口已锁定
BalanceReferenceMismatch|Нужна проверенная исходная база оригинального клиента|A verified original client base is required|需要验证过的原始客户端数据库
BalanceCoverageMismatch|Снимок несовместим с исходной базой|The snapshot is incompatible with the original base|快照与原始数据库不兼容
InvalidBalanceSnapshot|Снимок повреждён или имеет неизвестный формат|The snapshot is damaged or has an unsupported format|快照已损坏或格式不受支持
BalanceDownloadFailed|Не удалось загрузить баланс. Откройте сохранённый снимок или снимите галочку.|Balance download failed. Open a saved snapshot or uncheck Balance Council.|平衡数据下载失败。请打开已保存快照或取消勾选平衡议会。
BalanceCachedAfterFailure|Обновление не удалось; используется сохранённый снимок|Update failed; using the saved snapshot|更新失败；正在使用已保存快照
BalanceOldSnapshot|Снимку больше 35 дней|Snapshot is over 35 days old|快照已超过35天
BalanceCardMissing|Нет подтверждённых данных этой карты: сила и найм заблокированы|No verified data for this card: power and provisions are locked|此卡无已验证数据：战力和人口已锁定
BalanceRange|Это итоговое значение невозможно получить в допустимом диапазоне локального файла|This final value cannot be represented within the local file's supported range|此最终数值无法在本地文件允许的范围内表示
CouncilExportNote|Показаны локальные значения с накопленной поправкой Совета. При записи XML поправка вычитается. Для клиента без Совета снимите галочку.|Values include the accumulated Council adjustment. It is subtracted when writing XML. Uncheck for a client without Council patches.|显示的数值包含累计议会调整。写入 XML 时会减去调整值。不使用议会补丁的客户端请取消勾选。
LocalValues|Локальные сила / найм|Local power / provisions|本地战力 / 人口
BalanceAdjustment|Поправка силы / найма|Power / provision adjustment|战力 / 人口调整
BalanceSwitchConfirm|Уже изменены сила или найм. Их записанные XML-значения сохранятся, но показанные итоговые значения могут измениться. Продолжить?|Power or provisions have already changed. Stored XML values will be kept, but displayed final values may change. Continue?|战力或人口已修改。已存储的 XML 数值将保留，但显示的最终数值可能变化。是否继续？
Deploy|Размещение|Deploy|部署
Order|Приказ|Order|指令
Reaction|Триггер|Trigger|触发
MultipleEffects|Несколько эффектов|Multiple Effects|多个效果
Uses|Использований|Uses|使用处
NoAbilityNumbers|Нет доступных для изменения числовых параметров|No editable numeric ability parameters|没有可编辑的技能数值参数
ComputedValue|Значение вычисляется графом|The graph calculates this value|该值由技能图计算
RuntimeVariable|Переменная изменяется во время игры|This variable changes during gameplay|此变量在游戏过程中变化
ExternalValue|Значение используется внешней способностью|An external ability uses this value|外部技能使用此值
AmbiguousValue|Назначение значения не подтверждено|The purpose of this value is not verified|尚未验证此值的用途
UnusedValue|Не найдено использование значения|No use of this value was found|未找到此值的使用处
SharedAbility|Способность общая или связана с другой картой|The ability is shared or linked to another card|技能共享或关联到另一张卡牌
GraphHasErrors|Граф содержит ошибки: изменение заблокировано|The graph contains errors: editing is blocked|技能图有错误，已禁止编辑
AbilityRange|Значение вне проверенного диапазона|The value is outside the verified range|数值超出已验证范围
InvalidTargetLimits|Минимум целей не может превышать максимум|Minimum targets cannot exceed maximum targets|最少目标数不能大于最多目标数
InitialValueLocked|Начальное значение требует проверки синхронизации с графом|This initial value requires graph synchronization checks|此初始值需要验证与技能图的同步
TextLinked|Описание использует переменную|The description uses a variable|描述使用变量
CheckDescription|Проверьте числовой текст описания после изменения|Check literal numbers in the description after editing|修改后请检查描述中的固定数值
DescriptionBinding.Direct|Описание этого языка использует переменную|This language's description uses the variable|当前语言的描述使用此变量
DescriptionBinding.Formula|Описание этого языка использует формулу с переменной|This language's description uses a formula with the variable|当前语言的描述使用包含此变量的公式
DescriptionBinding.Unlinked|Связь с текстом не подтверждена; проверьте описание|Text linkage is unverified; review the description|未确认文本关联；请检查描述
DescriptionBinding.CaseMismatch|Регистр переменной в описании отличается; нужна проверка|Variable letter case differs in the description; review required|描述中的变量大小写不同；需要检查
DescriptionBinding.UnknownExpression|Выражение в описании не распознано; нужна проверка|Description expression is unrecognized; review required|无法识别描述表达式；需要检查
DescriptionBinding.MissingText|Описание на языке интерфейса недоступно|No description is available in the interface language|当前界面语言的描述不可用
DescriptionWarning|Проверьте описания: связь изменённых чисел с текстом не подтверждена. Автоматической замены текста нет. Языки:|Review descriptions: linkage of edited numbers to text is unverified. Text is not replaced automatically. Languages:|请检查描述：修改的数值与文本的关联未确认。不会自动替换文本。语言：
DescriptionAbsentWarning|В исходных данных нет описания для языков:|The source data has no description for these languages:|源数据缺少以下语言的描述：
Ability.damage|Урон|Damage|伤害
Ability.boost|Усиление|Boost|增益
Ability.heal|Исцеление|Healing|治疗
Ability.strengthen|Увеличение изначальной силы|Base Power Increase|基础战力提升
Ability.base-power-loss|Уменьшение изначальной силы|Base Power Reduction|降低基础战力
Ability.set-base-power|Установить изначальную силу|Set Base Power|设定基础战力
Ability.armor-gain|Получение брони|Armor Gain|获得护甲
Ability.armor-loss|Потеря брони|Armor Loss|失去护甲
Ability.set-armor|Установить броню|Set Armor|设定护甲
Ability.set-power|Установить силу|Set Power|设定战力
Ability.power-change|Изменение силы|Power Change|战力变化
Ability.armor-change|Изменение брони|Armor Change|护甲变化
Ability.timer-change|Изменение таймера|Timer Change|计时器变化
Ability.row-timer-change|Длительность эффекта ряда|Row Effect Duration|排效果持续时间
Ability.charge-change|Изменение зарядов|Charge Change|充能变化
Ability.min-targets|Минимум целей|Minimum Targets|最少目标数
Ability.max-targets|Максимум целей|Maximum Targets|最多目标数
Ability.targets|Количество целей|Target Count|目标数量
Ability.min-choices|Минимум выбранных карт|Minimum Card Choices|最少选择卡牌数
Ability.max-choices|Максимум выбранных карт|Maximum Card Choices|最多选择卡牌数
Ability.choices|Количество выбранных карт|Card Choice Count|选择卡牌数量
Ability.min-template-choices|Минимум карт из предложенных вариантов|Minimum Offered Card Choices|最少选择候选卡牌数
Ability.max-template-choices|Максимум карт из предложенных вариантов|Maximum Offered Card Choices|最多选择候选卡牌数
Ability.template-choices|Количество карт из предложенных вариантов|Offered Cards to Select|选择候选卡牌数量
Ability.min-row-targets|Минимум выбираемых рядов|Minimum Rows to Select|最少选择排数
Ability.max-row-targets|Максимум выбираемых рядов|Maximum Rows to Select|最多选择排数
Ability.row-targets|Количество выбираемых рядов|Rows to Select|选择排数
Ability.spawn-count|Количество создаваемых карт|Spawn Count|生成数量
Ability.draw-count|Количество добираемых карт|Draw Count|抽牌数量
Ability.reveal-count|Количество вскрываемых карт|Reveal Count|揭示数量
Ability.provision-limit|Порог стоимости найма|Provision Limit|人口上限
Ability.definition-provision-limit|Условие стоимости найма в списке карт|Card-list Provision Condition|卡牌列表人口条件
Ability.power-limit|Порог силы|Power Limit|战力阈值
Ability.armor-condition|Условие брони|Armor Condition|护甲条件
Ability.base-power-condition|Условие изначальной силы|Base Power Condition|基础战力条件
Ability.draw-selected-count|Количество карт: выбор и добор|Cards to Select and Draw|选择并抽取的卡牌数量
Ability.shuffle-into-deck-count|Количество карт для замешивания в колоду|Cards to Shuffle into the Deck|洗入牌组的卡牌数量
Ability.additional-destruction-count|Дополнительные отряды того же цвета|Additional Same-color Units|额外的同色单位数量
Ability.reveal-owner-count|Вскрытие карт владельца способности|Cards to Reveal for the Ability Owner|揭示技能拥有者的卡牌数量
Ability.reveal-opponent-count|Вскрытие карт противника|Opponent Cards to Reveal|揭示对手的卡牌数量
AbilityHelp.AdditionalDestruction|Первый отряд выбирается отдельно и задаёт цвет рамки. Это число — дополнительные отряды того же цвета для уничтожения. Значение 1 означает всего 2 отряда.|The first unit is selected separately to determine border color. This is the number of additional same-color units to destroy. A value of 1 means 2 units in total.|先单独选择一个单位以确定边框颜色。此数值是额外摧毁的同色单位数量。数值1表示总计2个单位。
AbilityHelp.RevealAfterSwap|Меняет только число карт, вскрываемых после обмена. Сам граф обмена перемещает по одной верхней карте колоды каждого игрока; это не настройка количества обмениваемых карт.|Changes only how many cards are revealed after the swap. The swap itself moves one top-deck card per player; this does not change how many cards are swapped.|仅修改交换后揭示的卡牌数量。交换本身移动双方牌组顶部各一张牌；此设置不改变交换数量。
AbilityHelp.RevealByPlayer|Количество вскрываемых карт для указанного игрока. Это не добор или обмен картами.|Cards revealed for the specified player. Not card draw or a swap.|为指定玩家揭示的卡牌数量，不是抽牌或交换卡牌。
Meaning.MovePlace.DeckRandom|Выбранные карты замешиваются в колоду: используется случайная позиция.|Selected cards are shuffled into the deck using a random position.|所选卡牌以随机位置洗入牌组。
Ability.move-to-deck-bottom-count|Количество карт для перемещения вниз колоды|Cards to Move to the Deck Bottom|移至牌组底部的卡牌数量
Ability.move-to-deck-top-count|Количество карт для перемещения наверх колоды|Cards to Move to the Deck Top|移至牌组顶部的卡牌数量
AbilityHelp.ArmorCondition|Сравнивается броня, не сила. Это условие отбора или проверки карты, а не количество изменяемой брони. При условии «больше 0» нужна хотя бы 1 ед. брони.|Compares armor, not power. A selection or test condition, not the amount of armor to change. Greater than 0 requires at least 1 armor.|比较的是护甲而非战力。这是筛选或检查条件，不是改变的护甲数量。大于0表示至少需要1点护甲。
AbilityHelp.BasePowerCondition|Сравнивается изначальная сила, не текущая. Это условие проверки, а не усиление.|Compares base power, not current power. A test condition, not a boost.|比较基础战力而非当前战力。这是检查条件，不是增益。
AbilityHelp.DrawSelected|Сколько карт выбрать и взять в руку. Выбор и фактический добор меняются вместе, чтобы эти числа не противоречили друг другу.|Cards to select and draw into hand. Selection and actual draw counts change together so they remain consistent.|选择并抽入手牌的卡牌数量。选择数量与实际抽牌数量同步修改，保持一致。
AbilityHelp.MoveChosenCards|Сколько выбранных карт переместить в колоду этим шагом. Это не дополнительный добор.|Selected cards to move into the deck in this step. Not additional card draw.|此步骤移入牌组的所选卡牌数量，不是额外抽牌。
Ability.discard-selected-count|Количество карт для сброса|Cards to Discard|丢弃卡牌数量
AbilityHelp.DiscardChosenCards|Сколько карт выбрать из своей руки и сбросить. Это не количество добираемых карт.|Cards to select from your hand and discard. Not the number of cards drawn.|从己方手牌中选择并丢弃的卡牌数量，不是抽牌数量。
Meaning.MovePlace.DeckBottom|Перемещение выбранных карт: вниз колоды.|Move selected cards to the bottom of the deck.|将所选卡牌移至牌组底部。
Meaning.MovePlace.DeckTop|Перемещение выбранных карт: наверх колоды.|Move selected cards to the top of the deck.|将所选卡牌移至牌组顶部。
Meaning.MovePlayer.Owner|Колода получателя: у владельца способности.|Destination deck: the ability owner's.|目标牌组：技能拥有者的牌组。
Meaning.MovePlayer.Opponent|Колода получателя: у противника владельца способности.|Destination deck: the ability owner's opponent's.|目标牌组：技能拥有者对手的牌组。
SurvivalCheck|Это служебная проверка выживания после урона. Изменение заблокировано.|This is an internal survival check after damage. Editing is blocked.|这是受伤后的内部存活检查。已禁止修改。
PowerExtremumSelection|Служебный отбор сильнейших или слабейших карт. Изменение заблокировано.|Internal strongest or weakest card selection. Editing is blocked.|内部最强或最弱卡牌筛选。已禁止修改。
ImplicitPowerCondition|Служебная проверка силы или брони без явного числового порога в описании. Изменение заблокировано.|Internal power or armor condition without an explicit numeric threshold in the description. Editing is blocked.|描述中没有明确数值门槛的内部战力或护甲检查。已禁止修改。
SingleStrengthSource|Для определения усиления используется только первый удалённый отряд. Число удаляемых карт фиксировано.|Only the first banished unit supplies the boost amount. The banish count is fixed.|强化数值仅取自第一个被放逐的单位。放逐数量固定。
UnusedOrderCondition|Служебная проверка неиспользованного приказа, а не число зарядов.|Internal unused-Order condition, not a charge count.|内部未使用指令检查，并非指令次数。
ImplicitChargeCondition|Служебная проверка отсутствия зарядов, а не начальное число зарядов.|Internal empty-charge check, not the initial charge count.|内部充能耗尽检查，并非初始充能数量。
SelfRevealPresentation|Служебное вскрытие самой карты. Количество фиксировано.|Internal reveal of the card itself. The count is fixed.|内部揭示此牌自身，数量固定。
ChargeActivationSignal|Нулевое усиление служит сигналом применения способности зарядов. Изменение заблокировано.|Zero boost is an internal signal for the charge ability. Editing is blocked.|零点增益是充能技能的内部信号，已禁止修改。
ArmorConversionReset|Обнуление брони при её преобразовании в усиление. Не самостоятельная величина эффекта.|Armor is cleared when converted into a boost. Not a separate effect amount.|将护甲转化为增益时清空护甲，并非独立效果数值。
ComputedEffectUnsupported|Структура вычисляемого эффекта или связанные числа не подтверждены. Изменение заблокировано.|The computed effect structure or linked values are not verified. Editing is blocked.|尚未验证计算效果结构或关联数值，已禁止修改。
Ability.first-target-damage|Урон выбранной цели|Damage to the Selected Target|对所选目标的伤害
Ability.spring-total-damage|Общий урон: Бросок|Total Damage: Spring|翻开：总伤害
Ability.gord-boost-cap|Лимит усиления за сыгранные особые карты|Boost Limit from Played Special Cards|已打出特殊牌提供的增益上限
MeaningHelp.GordBoostCap|Максимальное усиление Горда за особые карты, сыгранные за эту игру. Меняет фактическое ограничение счётчика и число в описании вместе; усиление за одну особую карту не меняется.|Maximum boost Gord gains from special cards played this game. Updates both the actual counter cap and the description; the boost per special card is unchanged.|戈德从本局已打出的特殊牌获得的最大增益。同时更新实际计数上限与描述中的数值，不改变每张特殊牌提供的增益。
DestructionPresentation|Нулевой удар перед уничтожением отрядов. Не самостоятельный урон способности.|Zero-damage hit before destroying units. Not a separate ability damage amount.|摧毁单位前的零伤害命中，并非独立的技能伤害数值。
MeaningHelp.SpringTotalDamage|Суммарный урон Броска распределяется случайными попаданиями по 1 ед. Урон Засады определяется стоимостью найма сыгранной противником карты и этой настройкой не меняется.|Total Spring damage is distributed as random 1-point hits. Ambush damage comes from the opponent's played card provision cost and is not changed by this setting.|翻开的总伤害以每次1点随机分配。伏击伤害取决于对手打出卡牌的人口，不受此设置影响。
Ability.nature-shared-targets|Количество целей всех трёх вариантов|Targets for All Three Options|三个选项共用的目标数量
Ability.barnabas-boost|Усиление эльфа, краснолюда и дриады|Boost for the Elf, Dwarf and Dryad|精灵、矮人与树精的增益
AbilityHelp.BarnabasBoost|Одинаковое усиление для каждого выбранного отряда. Три последовательных выбора не закреплены за типами: выбирайте эльфа, краснолюда и дриаду в любом порядке, по одному отряду каждого типа.|The same boost applies to every selected unit. The three selections are not tied to specific categories: choose an Elf, Dwarf and Dryad in any order, one unit of each category.|对每个所选单位给予相同增益。三次选择不固定对应类别：可按任意顺序各选择一个精灵、矮人与树精。
AbilityHelp.NatureSharedTargets|Общая настройка Единения с природой: меняет количество целей у Покрова, Усиления и Выносливости одновременно. Усиление и статус применяются к каждой выбранной цели.|Shared Shaping Nature setting: changes the target count for Veil, Boost and Vitality together. The boost and status apply to every selected target.|自然的形塑共用设置：同时更改帷幕、增益和活力选项的目标数量。增益与状态作用于每个所选目标。
ChoiceEffectOwned|Настройка доступна у соответствующей карты выбора.|This setting belongs to the corresponding choice card.|请在对应选项卡牌中调整此设置。
RandomDamageQuantum|Технический размер одного попадания при случайном распределении урона.|Technical size of one hit in randomly distributed damage.|随机分配伤害时单次命中的技术数值。
Ability.random-series-start|Урон первого случайного удара|First Random Hit Damage|首次随机伤害
Ability.linked-elephant-boost|Усиление за один смежный отряд|Boost per Adjacent Unit|每个相邻单位的增益
MeaningHelp.FirstTargetDamage|Урон первого удара по выбранной цели. Последующие случайные удары настраиваются отдельно.|Damage of the first hit to the selected target. Subsequent random hits are configured separately.|对所选目标首次造成的伤害。后续随机伤害单独设置。
MeaningHelp.RandomDamageSeries|Начальный урон серии из трёх случайных ударов. Каждый следующий удар слабее на 1: например, 5 означает 5, 4, 3. Связанные числа в описании меняются вместе. Число ударов не меняется.|Starting damage of three random hits, each 1 lower than the previous: 5 means 5, 4, 3. Linked description values update together. The number of hits is unchanged.|三次随机伤害的初始值，后续每次减少1点。例如5表示5、4、3。描述中的关联数值同步更新，不改变伤害次数。
MeaningHelp.LinkedElephantBoost|Усиление за одного смежного отряда. Два смежных отряда и Подкрепление используют удвоенное значение: например, 5 и 10. Одна настройка меняет оба числа; независимо менять усиление Подкрепления в этом графе нельзя.|Boost for one adjacent unit. Two adjacent units and Crew use twice this value, e.g. 5 and 10. One setting updates both amounts; Crew's boost cannot be changed independently in this graph.|一个相邻单位对应的增益。两个相邻单位和操控使用双倍数值，例如5和10。此设置同步修改两个数值，无法在该效果结构中单独修改操控增益。
Ability.strength-transfer-targets|Количество усиливаемых отрядов|Units to Boost|强化单位数量
AbilityHelp.AlternativeStrengthTransfer|Одна настройка для обеих альтернативных ветвей способности. Удаляется один отряд; каждый выбранный дружественный отряд получает усиление на его силу. Условная ветвь расширяет доступный цвет рамки, а не добавляет второе применение.|One setting for both alternative branches. One unit is banished; each selected allied unit is boosted by its power. The conditional branch expands the eligible border color, not the number of activations.|两条互斥分支共用此设置。放逐一个单位，每个选中的友军单位均获得等同于其战力的增益。条件分支扩展可选边框颜色，而非增加触发次数。
Ability.power-level|Отбор по рангу силы|Power Ranking Selection|按战力排名筛选
Ability.highest-power-count|Количество сильнейших отрядов|Highest-power Unit Count|最强单位数量
Ability.highest-power-levels|Количество уровней силы от наибольшего|Power Levels from Highest|从最高开始的战力层级数
Ability.lowest-power-count|Количество слабейших отрядов|Lowest-power Unit Count|最弱单位数量
Ability.lowest-power-levels|Количество уровней силы от наименьшего|Power Levels from Lowest|从最低开始的战力层级数
AbilityHelp.HighestCount|Сколько отрядов отобрать, начиная с самых сильных. Это не величина урона и не порог силы.|How many units to select, starting with the strongest. Not damage or a power threshold.|从最强单位开始筛选的数量。不是伤害值或战力阈值。
AbilityHelp.LowestCount|Сколько отрядов отобрать, начиная с самых слабых. Это не величина урона и не порог силы.|How many units to select, starting with the weakest. Not damage or a power threshold.|从最弱单位开始筛选的数量。不是伤害值或战力阈值。
AbilityHelp.HighestLevels|При 1 отбираются отряды с максимальной силой. Поле задаёт количество уровней силы для отбора, а не урон или число получателей урона. Дальнейший выбор цели выполняется самой способностью.|At 1, selects units with the highest power. Sets the number of power levels considered, not damage or the number of units damaged. The ability then chooses its target.|为1时筛选战力最高的单位。设置的是筛选的战力层级数，不是伤害值或受伤单位数量。随后由技能选择目标。
AbilityHelp.LowestLevels|При 1 отбираются отряды с минимальной силой. Поле задаёт количество уровней силы для отбора, а не урон или число получателей урона. Дальнейший выбор цели выполняется самой способностью.|At 1, selects units with the lowest power. Sets the number of power levels considered, not damage or the number of units damaged. The ability then chooses its target.|为1时筛选战力最低的单位。设置的是筛选的战力层级数，不是伤害值或受伤单位数量。随后由技能选择目标。
AbilityHelp.SelectedRows|Количество рядов, выбираемых игроком для способности. Это не число отрядов в ряду.|Number of rows the player selects for the ability. Not the number of units on a row.|玩家为技能选择的排数，不是排中的单位数量。
Ability.charges-limit|Порог зарядов|Charge Limit|充能阈值
Ability.timer-limit|Порог таймера|Timer Limit|计时器阈值
Ability.bloodthirst|Жажда крови|Bloodthirst|战狂
Ability.ability-armor|Броня способности|Ability Armor|技能护甲
Ability.ability-charges|Заряды способности|Ability Charges|技能充能
Ability.ability-charge|Заряд способности|Ability Charge|技能充能
Ability.cooldown|Восстановление|Cooldown|冷却
Ability.ability-timer|Таймер способности|Ability Timer|技能计时器
Ability.duration|Длительность|Duration|持续时间
Ability.bleeding-duration|Длительность кровотечения|Bleeding Duration|重伤持续时间
Ability.vitality-duration|Длительность выносливости|Vitality Duration|活力持续时间
Ability.coin-gain|Получение монет|Coin Gain|获得金币
Ability.profit|Выгода|Profit|利润
Ability.base-profit|Базовая выгода|Base Profit|基础利润
Ability.tribute-cost|Стоимость дани|Tribute Cost|献金费用
Ability.fee-cost|Стоимость платежа|Fee Cost|费用
Ability.berserk-threshold|Порог берсерка|Berserk Threshold|狂暴阈值
Ability.adrenaline-threshold|Адреналин|Adrenaline|亢奋
Ability.intimidate-boost|Усиление устрашения|Intimidate Boost|恐吓增益
Ability.copy-count|Количество создаваемых копий|Spawned Copy Count|生成复制数量
Ability.copy-count-owner|Количество копий у владельца способности|Copies for the Ability Owner|为技能拥有者生成的复制数量
Ability.copy-count-opponent|Количество копий у противника|Copies for the Opponent|为对手生成的复制数量
Ability.spawn-count-owner|Количество новых карт у владельца способности|New Cards for the Ability Owner|为技能拥有者生成的新卡牌数量
Ability.spawn-count-owner-melee|Количество новых карт в вашем ближнем ряду|New Cards in Your Melee Row|在己方近战排生成的新卡牌数量
Ability.spawn-count-owner-ranged|Количество новых карт в вашем дальнем ряду|New Cards in Your Ranged Row|在己方远程排生成的新卡牌数量
OrderActivationSignal|Служебный сигнал активации приказа|Internal Order activation signal|内部指令激活信号
ImmunityArmorBookkeeping|Служебная обработка неизменяемой силы|Internal immutable-power bookkeeping|固定战力的内部处理
SelectionColorAnchor|Первый отряд определяет цвет рамки|First unit determines the border color|第一个单位决定边框颜色
Ability.spawn-count-opponent|Количество новых карт у противника|New Cards for the Opponent|为对手生成的新卡牌数量
Initial|Начальное состояние|Initial State|初始状态
BaseValue|Базовое значение|Base Value|基础数值
AlternativeValue|Альтернативное значение|Alternative Value|替代数值
Threshold|Порог|Threshold|阈值
Gain|Получение|Gain|获得
DescriptionNotChanged|Описание карты не изменяется автоматически|The card description is not updated automatically|卡牌描述不会自动更新
ZealNeedsOrder|Нужен существующий приказ отряда с начальными зарядами|Requires an existing unit Order with initial charges|需要已有指令且具有初始充能的单位
GraphControlsZeal|Граф сам изменяет готовность приказа в ход появления|The graph controls same-turn Order readiness|技能图控制登场回合的指令可用性
UnsupportedZealGraph|Совместимость переключателя с этим графом пока не подтверждена|This graph is not yet verified for this toggle|尚未验证此技能图与该开关的兼容性
SummoningSickness|Начальное Рвение|Initial Zeal|初始狂热
Keyword.intimidate|Устрашение|Intimidate|恐吓
Keyword.deploy|Размещение|Deploy|部署
Keyword.order|Приказ|Order|指令
Keyword.zeal|Рвение|Zeal|狂热
Keyword.adrenaline|Адреналин|Adrenaline|亢奋
Keyword.berserk|Берсерк|Berserk|狂暴
Keyword.bloodthirst|Жажда крови|Bloodthirst|战狂
Keyword.bleeding|Кровотечение|Bleeding|重伤
Keyword.vitality|Выносливость|Vitality|活力
Keyword.profit|Выгода|Profit|利润
Keyword.resupply|Восполнение|Resupply|增兵
Keyword.echo|Эхо|Echo|回响
Keyword.flanking|Обход|Flanking|翼守
Keyword.insanity|Безумие|Insanity|癫狂
Keyword.tribute|Дань|Tribute|献金
Keyword.fee|Платеж|Fee|费用
Keyword.coin|Монета|Coin|金币
Keyword.cooldown|Восстановление|Cooldown|冷却
Keyword.charge|Заряды|Charge|充能
Ability.count|Количество|Count|数量
Ability.counter|Счётчик|Counter|计数器
Ability.ability-provision|Стоимость способности|Ability Provisions|技能人口
Ability.ability-power|Сила способности|Ability Power|技能战力
Ability.cost|Стоимость|Cost|费用
Ability.amount|Количество|Amount|数值
Ability.value|Значение|Value|数值
Ability.initial-charges|Начальные заряды|Initial Charges|初始充能
Ability.initial-patience|Начальная выдержка|Initial Patience|初始耐心
MeaningHelp.InitialPatience|Начальное значение счётчика выдержки этой карты, до его роста. Может определять урон, усиление, длительность, число карт или стоимость создаваемого заклятия. Прирост выдержки за ход не меняется.|The card's initial Patience counter before it grows. It may determine damage, boost, duration, card count, or the provision cost of a created Spell. Does not change Patience growth per turn.|此牌在增长前的初始耐心数值，可能决定伤害、增益、持续时间、卡牌数量或生成法术的招募费用。不改变每回合耐心的增长量。
Ability.initial-timer|Начальный таймер|Initial Timer|初始计时器
Open|Открыть клиент GWENT|Open GWENT Client|打开昆特牌客户端
OpenClient|Выберите папку клиента GWENT|Select the GWENT client folder|选择昆特牌客户端文件夹
CardGroup|Категория|Category|分类
MainCards|Карты|Cards|卡牌
Leaders|Лидеры|Leaders|领袖
Leader|Лидер|Leader|领袖
LeaderProvision|Бонус найма колоды|Deck Provision Bonus|卡组人口加成
LeaderLocalBalance|Лидер: локальный бонус найма|Leader: Local Provision Bonus|领袖：本地人口加成
LeaderLocalNote|Бонус найма лидера берётся из локальных файлов, независимо от Совета Баланса.|Leader provisions use local values independently of Balance Council.|领袖人口加成使用本地数值，不受平衡议会模式影响。
LeaderFieldsLocked|У лидера доступны бонус найма, проверенные числа и тексты; остальные параметры заблокированы.|Only leader provision bonus, verified numbers and texts can be edited.|领袖仅可修改人口加成、已验证数值和文本。
LeaderNumberUnsupported|Этот параметр лидера пока не поддерживается.|This leader parameter is not supported yet.|暂不支持此领袖参数。
Symbols|Символы (токены)|Tokens|衍生物
TokenLocalBalance|Токен: локальные параметры|Token: Local Values|衍生物：本地数值
TokenLocalNote|Параметры токена берутся из локальных файлов. Создающая карта может менять их при появлении.|Token values come from local files. The creating card may modify them on spawn.|衍生物使用本地数值，生成它的卡牌可能在生成时修改这些数值。
AllCards|Все|All|全部
RelatedCards|Связанные карты|Related Cards|关联卡牌
OpenCard|Открыть карту|Open Card|打开卡牌
Relation|Связь|Relation|关联
ReferencedBy|Ссылается на текущую карту|References the current card|引用当前卡牌
ReferencesCard|Указана в текущей способности|Referenced in the current ability|当前技能中提及
ReferenceNote|Показаны прямые ссылки на карты. Они не всегда означают создание; динамические и вложенные связи могут отсутствовать.|Direct card references are shown. They do not always mean spawning; dynamic and nested links may be absent.|仅显示直接卡牌引用。引用不一定表示生成；动态及嵌套关联可能未列出。
Appearance|Оформление|Appearance|外观
Theme|Тема|Theme|主题
BackgroundColor|Цвет фона|Background Color|背景颜色
AccentColor|Цвет выделения|Selection Color|选中颜色
Font|Шрифт|Font|字体
Light|Светлая|Light|浅色
Dark|Тёмная|Dark|深色
Custom|Своя|Custom|自定义
ClientNotFound|Не найден data_definitions. Выберите папку клиента GWENT.|data_definitions was not found. Choose a GWENT client folder.|未找到 data_definitions。请选择昆特牌客户端目录。
TutorialHidden|Карты обучения скрыты.|Tutorial cards are hidden.|已隐藏教程卡牌。
Export|Экспорт|Export|导出
Undo|Отменить|Undo|撤销
Reset|Сбросить всё|Reset All|全部重置
Apply|Применить изменения|Apply Changes|应用更改
Revert|Сбросить поля|Revert Fields|还原字段
Search|Поиск: название или ID|Search: name or ID|搜索名称或 ID
Sort|Сортировка|Sort|排序
Name|Название|Name|名称
Rarity|Редкость|Rarity|稀有度
Power|Сила|Power|战力
Armor|Броня|Armor|护甲
Provision|Стоимость найма|Provisions|人口
ShortProvision|Найм|Prov.|人口
Tier|Рамка|Border|边框
FactionId|Фракция|Faction|阵营
SecondaryFactionId|Вторая фракция|Secondary Faction|次要阵营
Categories|Категории|Categories|类别
PrimaryCategory|Основная категория|Primary Category|主要类别
AbilityValueFallback|Значение способности|Ability value|技能数值
None|Нет|None|无
KeepOriginal|Сохранить исходную|Keep Original|保留原值
Texts|Тексты карты|Card Text|卡牌文本
Parameters|Параметры|Parameters|参数
tooltip|Описание способности|Ability Description|技能描述
fluff|Художественный текст|Flavor Text|背景描述
name|Название|Name|名称
Hidden|Показать скрытые|Show Hidden|显示隐藏卡牌
Neutral|Нейтральные|Neutral|中立
Monsters|Чудовища|Monsters|怪兽
NorthernRealms|Королевства Севера|Northern Realms|北方领域
Scoiatael|Скоя'таэли|Scoia'tael|松鼠党
Skellige|Скеллиге|Skellige|史凯利格
Nilfgaard|Нильфгаард|Nilfgaard|尼弗迦德
Syndicate|Синдикат|Syndicate|辛迪加
Common|Обычная|Common|普通
Rare|Редкая|Rare|稀有
Epic|Эпическая|Epic|史诗
Legendary|Легендарная|Legendary|传说
Bronze|Бронзовая|Bronze|铜色
Gold|Золотая|Gold|金色
Unit|Отряд|Unit|单位
Special|Особая|Special|特殊
Artifact|Артефакт|Artifact|神器
Other|Служебная карта|Other Card|其他卡牌
NoProject|Клиент GWENT не открыт|No GWENT Client Open|未打开昆特牌客户端
NoCards|Карты не найдены|No Cards Found|未找到卡牌
NoSelection|Выберите карту|Select a Card|选择卡牌
Changes|Изменений|Changes|修改
Cards|Карт|Cards|卡牌
Ready|Без изменений|No Changes|无修改
Error|Ошибка|Error|错误
Confirm|Подтвердите изменения|Confirm Changes|确认修改
Before|Было|Before|修改前
After|Стало|After|修改后
Continue|Продолжить?|Continue?|继续？
Cancel|Отмена|Cancel|取消
Discard|Есть несохранённые изменения. Отбросить их?|There are unsaved changes. Discard them?|有未保存的修改。要放弃吗？
ResetConfirm|Сбросить все изменения? Действие можно отменить.|Reset all changes? This can be undone.|重置所有更改？此操作可撤销。
ExportFolder|Выберите родительскую папку для нового экспорта|Choose a parent folder for a new export|选择新导出文件夹的上级目录
Exported|Экспорт создан. Исходный клиент не изменён.|Export created. The source client is unchanged.|导出已创建，源客户端未修改。
ClientExported|Экспорт создан. Для установки нужен только data_definitions в корне папки. Папка _editor содержит резервную копию и отчёт: её в игру не копируйте. Исходный клиент не изменён.|Export created. Install only data_definitions from the top level. The _editor folder contains the backup and report: do not copy it into the game. The source client is unchanged.|导出已创建。安装时只需复制根目录中的 data_definitions。_editor 文件夹包含备份和报告，请勿复制到游戏目录。源客户端未修改。
Source|Источник|Source|来源
Output|Результат|Output|输出
MissingLocale|В клиенте нет файла этого языка. Тексты недоступны.|This language file is not present in the client. Text editing is unavailable.|客户端缺少此语言文件，无法编辑文本。
MissingText|У карты нет этого текста в файле локализации.|This card has no such text entry in the language file.|语言文件中没有此卡牌的该文本条目。
TextSafety|Сохраняются служебные теги и переменные. Для переноса строки: \n.|Formatting tags and variables must be preserved. Line break: \n.|须保留格式标签和变量。换行请使用 \n。
Scope|Основные параметры. Экспорт отдельно от исходных файлов.|Basic parameters. Exports never overwrite source files.|基本参数。导出不会覆盖源文件。
HiddenOrGenerated|Скрытая служебная запись: редактирование заблокировано.|Hidden service entry: editing is blocked.|隐藏的内部条目：已禁止编辑。
UnsupportedType|Тип этой карты не поддерживается базовым редактором.|This card type is not supported by the basic editor.|基本编辑器不支持此卡牌类型。
UnsupportedFields|Структура или значения карты не распознаны: редактирование заблокировано.|Unrecognized card structure or values: editing is blocked.|无法识别卡牌结构或数值：已禁止编辑。
SecondaryFactionInvalid|Основная и вторая фракции должны различаться.|The primary and secondary factions must be different.|主要阵营和次要阵营不能相同。
UnknownCategory|Неизвестная категория.|Unknown category.|未知类别。
MissingLocaleError|Файл локализации недоступен.|Language file is unavailable.|语言文件不可用。
MissingTextError|Эта запись локализации недоступна.|This language entry is unavailable.|此语言条目不可用。
PhysicalLine|Текст должен занимать одну физическую строку. Используйте \n вместо Enter.|Text must occupy one physical line. Use \n instead of Enter.|文本必须为单行。请使用 \n，不要按回车。
EmptyName|Название карты не может быть пустым.|Card name cannot be empty.|卡牌名称不能为空。
TextTokens|Нельзя менять или удалять служебные теги и переменные в базовом редакторе.|The basic editor cannot change or remove formatting tags or variables.|基本编辑器不允许修改或删除格式标签及变量。
NoChanges|Нет изменений для экспорта.|There are no changes to export.|没有可导出的修改。
ExportExists|Выходная папка уже существует. Выберите новый путь.|The output folder already exists. Choose a new path.|输出文件夹已存在，请选择新路径。
ExportInsideSource|Нельзя экспортировать внутрь папки клиента.|Cannot export inside the client folder.|不能导出到客户端文件夹内。
SourceChanged|Исходные файлы изменены вне текущей сессии. Операция остановлена для защиты внешних правок. Если есть несохранённые изменения, не закрывайте редактор.|Source files changed outside this session. The operation was stopped to protect external changes. Do not close the editor if you have unsaved changes.|源文件在当前会话之外发生了变化。为保护外部修改，操作已停止。如果有未保存的修改，请勿关闭编辑器。
Range|Недопустимое значение поля|Invalid field value|字段值无效
UnknownBits|Нераспознанные категории сохранены без изменений.|Unrecognized category bits are preserved unchanged.|保留未识别的类别数据。
""".Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r').Split('|'))
        .ToDictionary(p => p[0], p => p.Skip(1).ToArray());

    private static readonly string[] CategoryRows = """
nature|Природа|Nature|自然
warfare|Военное дело|Warfare|战法
alchemy|Алхимия|Alchemy|炼金
potion|Зелье|Potion|药剂
organic|Органика|Organic|有机
crime|Преступление|Crime|罪行
raid|Набег|Raid|突袭
tactic|Тактика|Tactic|谋略
spell|Заклятие|Spell|法术
human|Человек|Human|人类
bandit|Бандит|Bandit|匪徒
mage|Чародей|Mage|法师
soldier|Солдат|Soldier|士兵
warrior|Воин|Warrior|战士
witcher|Ведьмак|Witcher|猎魔人
elf|Эльф|Elf|精灵
dwarf|Краснолюд|Dwarf|矮人
dryad|Дриада|Dryad|树精
naiad|Наяда|Naiad|水精
beast|Зверь|Beast|野兽
vampire|Вампир|Vampire|吸血鬼
insectoid|Инсектоид|Insectoid|类虫生物
wild hunt|Дикая Охота|Wild Hunt|狂猎
ogroid|Огр|Ogroid|食人魔
relict|Реликт|Relict|残物
cursed|Проклятый|Cursed|诅咒生物
knight|Рыцарь|Knight|骑士
pirate|Пират|Pirate|海盗
ship|Корабль|Ship|船
druid|Друид|Druid|德鲁伊
agent|Агент|Agent|密探
aristocrat|Аристократ|Aristocrat|贵族
treant|Энт|Treant|树人
machine|Машина|Machine|机械
construct|Голем|Construct|构造体
specter|Призрак|Specter|幽灵
dragon|Дракон|Dragon|龙
harpy|Гарпия|Harpy|鹰身女妖
firesworn|Дети Огня|Firesworn|火誓者
crownsplitters|Златорубы|Crownsplitters|黑金会
cutups|Сердцееды|Cutups|鬼面帮
tidecloaks|Тени прилива|Tidecloaks|踏浪帮
blindeyes|Невидимые|Blindeyes|暗巷帮
salamandra|Саламандра|Salamandra|火蜥蜴帮
""".Split('\n', StringSplitOptions.RemoveEmptyEntries);

    static UiText()
    {
        foreach (string row in CategoryRows)
        {
            string[] parts = row.TrimEnd('\r').Split('|');
            Labels["category/" + parts[0]] = parts.Skip(1).ToArray();
        }
    }
    public static string Get(string key)
    {
        if (Language is not ("ru-ru" or "en-us" or "zh-cn") && Translation(Language).TryGetValue(key, out string? translated) && !string.IsNullOrWhiteSpace(translated)) return translated;
        return Labels.TryGetValue(key, out var values) ? values[Language == "ru-ru" ? 0 : Language == "zh-cn" ? 2 : 1] : key;
    }
    public static string Error(Exception e)
    {
        string key = e.Message;
        if (key.StartsWith("Range:")) return Get("Range") + ": " + Get(key[6..]);
        if (key == "MissingLocale") key = "MissingLocaleError";
        if (key == "MissingText") key = "MissingTextError";
        return Get(key);
    }
    public static IReadOnlyList<string> Keys => Labels.Keys.ToArray();
    public static string English(string key) => Labels.TryGetValue(key, out var values) ? values[1] : key;
    public static IReadOnlyDictionary<string, string> EnglishTemplate => Labels.ToDictionary(p => p.Key, p => p.Value[1]);
}
