using System.Text;

namespace GwentCardEditor;

public sealed record ReadinessSetting(int CardId, string NameRu, int[] NumberIds, string Role, string LabelRole,
    string Context, string MeaningEvidence, string TextStatus, string Status, int Priority, string[] LinkedLanguages, string[] UnlinkedLanguages);
public sealed record ReadinessGroup(string Status, int Priority, string Role, string LabelRole, string Context,
    int Settings, int Cards, int[] CardIds, string[] Examples);
public sealed record ReadinessReport(string Version, string Source, string? ArchiveHash, int Cards, int EditableValues,
    int DisplayedSettings, int SemanticLocks, IReadOnlyDictionary<string, int> Statuses,
    IReadOnlyList<ReadinessGroup> Groups, IReadOnlyList<ReadinessSetting> Settings);

public static class ReadinessAudit
{
    public static ReadinessReport Create(AbilityMeaningAuditReport source)
    {
        var settings = source.Rows.Select(row =>
        {
            var present = row.Bindings.Where(b => b.Kind != "MissingText").ToArray();
            bool allLinked = present.Length > 0 && present.All(b => b.Linked);
            string text = present.Length == 0 ? "NoDescription" : allLinked ? "AllPresentLinked"
                : present.Any(b => b.Linked) ? "PartiallyLinked" : "Unlinked";
            string status = present.Length == 0 ? "MissingDescription"
                : row.Evidence == "UnknownRole" || row.Evidence == "ContextNeedsReview" && !allLinked ? "ContextReview"
                : allLinked ? "StructurallyLinked" : "TextReview";
            bool plainEffect = row.LabelRole == row.Role && row.Role is "damage" or "boost" or "base-damage" or "coin-gain" or "armor-gain" or "strengthen";
            int priority = status == "ContextReview" ? plainEffect ? 1 : 2 : status == "StructurallyLinked" ? 4 : 3;
            return new ReadinessSetting(row.CardId, row.NameRu, row.NumberIds.ToArray(), row.Role, row.LabelRole, row.Context,
                row.Evidence, text, status, priority, present.Where(b => b.Linked).Select(b => b.Language).ToArray(),
                present.Where(b => !b.Linked).Select(b => b.Language).ToArray());
        }).ToArray();
        var groups = settings.GroupBy(r => (r.Status, r.Priority, r.Role, r.LabelRole, r.Context))
            .Select(g => new ReadinessGroup(g.Key.Status, g.Key.Priority, g.Key.Role, g.Key.LabelRole, g.Key.Context,
                g.Count(), g.Select(r => r.CardId).Distinct().Count(), g.Select(r => r.CardId).Distinct().Order().ToArray(),
                g.DistinctBy(r => r.CardId).Take(5).Select(r => $"{r.NameRu} ({r.CardId})").ToArray()))
            .OrderBy(g => g.Priority).ThenByDescending(g => g.Settings).ThenBy(g => g.Role, StringComparer.Ordinal).ThenBy(g => g.Context, StringComparer.Ordinal).ToArray();
        return new("1.0", source.Source, source.ArchiveHash, source.Cards, source.EditableValues, source.DisplayedSettings,
            source.SemanticLocks.Count, settings.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count()), groups, settings);
    }

    public static string RenderQueue(ReadinessReport report)
    {
        var md = new StringBuilder("# Очередь проверки параметров\n\n");
        md.AppendLine($"Карт с числовыми настройками: {report.Cards}. Видимых настроек: {report.DisplayedSettings}; связанных числовых значений: {report.EditableValues}.\n");
        md.AppendLine("Это структурный аудит исходной базы, не результат проверки всех карт в игре. Строка очереди не означает найденную ошибку. Аудит не меняет доступность параметров и не разблокирует служебные значения.\n");
        md.AppendLine("## Сводка\n\n| Состояние | Настроек |\n|---|---:|");
        foreach (var status in report.Statuses.OrderBy(p => p.Key, StringComparer.Ordinal)) md.AppendLine($"| {Title(status.Key)} | {status.Value} |");
        md.AppendLine($"\nОтдельно остаются заблокированными {report.SemanticLocks} распознанных служебных значений. Они не включены в число видимых настроек. Отсутствующие в исходнике переводы не считаются ошибками существующих переводов.\n");
        md.AppendLine("## Приоритеты\n\n1. Уточнить смысл неподписанных величин урона, усиления, брони и монет. Нулевое значение само по себе не доказывает служебную роль.\n2. Разбирать семейства выбора, создания и копирования по графам, а не по одной карте за раз.\n3. Проверять текст отдельно от механики: отсутствие подстановки числа не означает неверную работу эффекта.\n4. Подтверждённые структурой и текстом настройки проверять в игре по отдельному чек-листу.\n");
        md.AppendLine("## Группы для разбора\n\nТехнические коды ролей оставлены здесь для воспроизводимого поиска в JSON, но не являются подписями интерфейса. Приоритет 1 выше приоритета 2.\n\n| Приоритет | Состояние | Роль | Контекст | Полей / карт | Примеры |\n|---:|---|---|---|---:|---|");
        foreach (var group in report.Groups.Where(g => g.Status != "StructurallyLinked"))
            md.AppendLine($"| {group.Priority} | {Title(group.Status)} | `{group.LabelRole}` | `{group.Context}` | {group.Settings} / {group.Cards} | {string.Join(", ", group.Examples).Replace("|", "\\|", StringComparison.Ordinal)} |");
        md.AppendLine("\nЧисло карт нельзя складывать между группами: одна карта может иметь несколько настроек. Подробные ID чисел и языки без привязки находятся в readiness-summary.json; графы и исходные тексты — в ability-meanings.json.\n");
        return md.ToString();
    }

    public static string RenderGameplayChecklist(ReadinessReport report)
    {
        var md = new StringBuilder("# Короткий чек-лист игровой проверки\n\n");
        md.AppendLine("Все пункты ниже требуют ручной проверки в игре. Наличие пункта и успешный автоматический тест не означают, что он уже выполнен. Начинать с отдельной копии клиента и отдельных пакетов; не объединять эксперимент с MyProject.\n");
        md.AppendLine("Имена и ID ниже — примеры из исходной базы GWENT. В модифицированном или неполном проекте сначала проверить наличие карты и доступность нужной настройки; не обходить блокировку несовместимого графа.\n");
        md.AppendLine("Перед каждым тестом записать исходные значения, язык и включён ли Совет Баланса. Менять одну настройку, если пункт явно не проверяет совместную правку. Сохранить пакет и путь его резервной копии.\n");
        md.AppendLine("- [ ] **Основные характеристики.** На обычном отряде отдельно изменить силу, броню и найм. Сверить числа в коллекции, конструкторе и партии; затем выполнить откат.\n");
        foreach (string role in new[] { "damage", "boost" })
        {
            var sample = report.Settings.FirstOrDefault(r => r.Status == "StructurallyLinked" && r.Role == role && r.Context is "Deploy" or "Order");
            md.AppendLine($"- [ ] **{(role == "damage" ? "Урон" : "Усиление")}.** {(sample == null ? "Выбрать настройку с подтверждённой связью текста." : $"Пример из аудита: {sample.NameRu} ({sample.CardId}), {sample.Context}.")} Увеличить значение на 1; сверить описание и фактический результат, не меняя число целей.\n");
        }
        md.AppendLine("- [ ] **Выбранный добор.** Миргтабракке (112205), Проклятый свиток (202498), Маневр (202187), Селективная мутация (202782): поочерёдно поставить добор 2. Проверить размер выбора, число полученных карт, сохранение категории/фильтра и прежний возврат одной карты в колоду.\n");
        md.AppendLine("- [ ] **Перенос из руки.** Неофит из Виковаро (122403), Бродячий торговец (202215), Проклятый свиток (202498), Миргтабракке (112205), Трисс: колдунья (122107), Маневр (202187), Додрик Люмарц (202326), Селективная мутация (202782): поставить перенос 2. Проверить фактическое место назначения и прежний добор; отдельно проверить порядок карт при переносе наверх/вниз.\n");
        md.AppendLine("- [ ] **Сброс.** Коралл (152107), Хэймэй: скальд (152308), Маска Уробороса (202512): поставить сброс 2 при доборе 1. Проверить число выбранных и сброшенных карт. Отдельно проверить реакцию Коралл и прежнее количество создаваемых Ворон.\n");
        md.AppendLine("- [ ] **Независимость настроек.** На Маневре или Селективной мутации одновременно поставить добор 3 и замешивание 2. Должны быть два отдельных выбора с соответствующими числами, без изменения создания отряда или зарядов.\n");
        md.AppendLine("- [ ] **Заряды, выдержка и восстановление.** Выбрать карты, где соответствующие поля доступны. По отдельности проверить стартовое значение, расход, восстановление и изменение после перехода хода/раунда согласно описанию карты.\n");
        md.AppendLine("- [ ] **Добавляемое ключевое слово.** На совместимой карте включить один доступный переключатель, задать его число, если оно доступно, вызвать условие из описания. Удалить добавленное слово и проверить восстановление исходной способности.\n");
        md.AppendLine("- [ ] **Локализации.** Повторить просмотр изменённой карты на русском, английском и китайском. Проверить числа, условия, названия ключевых слов и отсутствие Missing-tooltip. После возврата числа к исходному сравнить исходное описание.\n");
        md.AppendLine("- [ ] **Медиа и оформление.** Отдельно проверить статичный арт, рубашку, пользовательский звук и глобальное оформление. Применить два последовательных пакета оформления, откатить второй, затем первый; сверить исходный вид и звук.\n");
        md.AppendLine("- [ ] **Публичный пакет.** Проверить запуск из новой папки без исходников и путей разработчика, выбор чистого клиента, экспорт, применение и откат. Проверить понятность названий резервных копий и отсутствие лишних игровых данных в дистрибутиве.\n");
        md.AppendLine("Для каждого пункта фиксировать: версия редактора, клиент, язык, ID карты, исходное/новое значение, ожидаемый/фактический результат и путь к пакету. Только после успешной отдельной проверки переносить правку в творческий проект.\n");
        return md.ToString();
    }

    private static string Title(string status) => status switch
    {
        "ContextReview" => "Уточнить смысл/контекст",
        "TextReview" => "Уточнить связь с описанием",
        "MissingDescription" => "Нет исходного описания",
        "StructurallyLinked" => "Связь подтверждена структурой и текстом; игра не проверена",
        _ => status
    };
}
