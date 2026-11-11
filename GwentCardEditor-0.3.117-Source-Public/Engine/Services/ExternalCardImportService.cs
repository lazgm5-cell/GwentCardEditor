using AbilityAnalyzer.Models;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace AbilityAnalyzer.Services;

public static class ExternalCardImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static ExternalCardImportPreview BuildPreview(
        ExternalCardImportRecipe recipe,
        string recipePath,
        string sourceStreamingAssets,
        string targetStreamingAssets)
    {
        using ImportContext context = PrepareContext(
            recipe,
            recipePath,
            sourceStreamingAssets,
            targetStreamingAssets,
            mutateTargetWorkspace: false);
        return context.Preview;
    }

    public static ExternalCardImportResult ApplyAndSave(
        ExternalCardImportRecipe recipe,
        string recipePath,
        string sourceStreamingAssets,
        string targetStreamingAssets,
        string outputDirectory,
        bool overwrite)
    {
        string output = Path.GetFullPath(outputDirectory);
        string sourceRootForSafety = ResolveStreamingAssets(sourceStreamingAssets, "исходной игры");
        string targetRootForSafety = ResolveStreamingAssets(targetStreamingAssets, "текущего GWENT");
        EnsureSafeOutputDirectory(
            output,
            sourceRootForSafety,
            targetRootForSafety,
            Path.GetFullPath(recipePath));
        if (Directory.Exists(output))
        {
            if (!overwrite)
                throw new IOException($"Выходной каталог уже существует: {output}");
            Directory.Delete(output, recursive: true);
        }

        Directory.CreateDirectory(output);
        try
        {
            using ImportContext context = PrepareContext(
                recipe,
                recipePath,
                sourceStreamingAssets,
                targetStreamingAssets,
                mutateTargetWorkspace: true);

            string payload = Path.Combine(output, "payload");
            Directory.CreateDirectory(payload);

            string dataDefinitionsOutput = Path.Combine(payload, "data_definitions");
            RebuildZipPreservingEntries(
                context.TargetDataDefinitionsPath,
                context.TargetWorkspace,
                dataDefinitionsOutput);
            context.RegisterPayloadFile("data_definitions", dataDefinitionsOutput);

            SaveModifiedCatalogs(context, payload);
            CopyResources(context, payload);

            string reportPath = Path.Combine(output, "IMPORT_REPORT.md");
            string manifestPath = Path.Combine(output, "IMPORT_MANIFEST.json");
            string installPs1 = Path.Combine(output, "install_import.ps1");
            string uninstallPs1 = Path.Combine(output, "uninstall_import.ps1");

            WriteReport(context, reportPath);
            WriteManifest(context, manifestPath);
            WriteInstallerScripts(context, output, installPs1, uninstallPs1);
            WriteCmdWrapper(output, "INSTALL_IMPORT.cmd", "install_import.ps1");
            WriteCmdWrapper(output, "UNINSTALL_IMPORT.cmd", "uninstall_import.ps1");
            WriteCmdWrapper(output, "VERIFY_TARGET.cmd", "verify_target.ps1");
            WriteVerifyScript(context, Path.Combine(output, "verify_target.ps1"));

            return new ExternalCardImportResult
            {
                OutputDirectory = output,
                DataDefinitionsFile = dataDefinitionsOutput,
                ReportFile = reportPath,
                ManifestFile = manifestPath,
                InstallScript = installPs1,
                UninstallScript = uninstallPs1,
                Preview = context.Preview
            };
        }
        catch
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
            throw;
        }
    }

    private static ImportContext PrepareContext(
        ExternalCardImportRecipe recipe,
        string recipePath,
        string sourceStreamingAssets,
        string targetStreamingAssets,
        bool mutateTargetWorkspace)
    {
        ValidateRecipe(recipe);

        string sourceRoot = ResolveStreamingAssets(sourceStreamingAssets, "исходной игры");
        string targetRoot = ResolveStreamingAssets(targetStreamingAssets, "текущего GWENT");
        if (PathsOverlap(sourceRoot, targetRoot))
        {
            throw new InvalidOperationException(
                "Source и Target StreamingAssets должны быть разными установками.");
        }
        string sourceDataDefinitions = ResolveRequiredFile(sourceRoot, "data_definitions");
        string targetDataDefinitions = ResolveRequiredFile(targetRoot, "data_definitions");

        var context = new ImportContext(
            recipe,
            Path.GetFullPath(recipePath),
            sourceRoot,
            targetRoot,
            sourceDataDefinitions,
            targetDataDefinitions);

        try
        {
            ExtractZip(sourceDataDefinitions, context.SourceWorkspace);
            ExtractZip(targetDataDefinitions, context.TargetWorkspace);
            context.LoadDocumentsAndCatalogs();
            BuildPlanAndOptionallyMutate(context, mutateTargetWorkspace);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static void EnsureSafeOutputDirectory(
        string output,
        string sourceStreamingAssets,
        string targetStreamingAssets,
        string recipePath)
    {
        string recipeDirectory = Path.GetDirectoryName(recipePath)
            ?? Environment.CurrentDirectory;
        foreach ((string protectedPath, string label) in new[]
        {
            (sourceStreamingAssets, "Source StreamingAssets"),
            (targetStreamingAssets, "Target StreamingAssets"),
            (recipeDirectory, "каталог рецепта")
        })
        {
            if (PathsOverlap(output, protectedPath))
            {
                throw new InvalidOperationException(
                    $"Выходной каталог пересекается с защищённым путём ({label}): " +
                    $"output={output}, protected={protectedPath}.");
            }
        }
    }

    private static bool PathsOverlap(string left, string right)
    {
        string leftFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        string rightFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(leftFull, rightFull, comparison))
            return true;
        string separator = Path.DirectorySeparatorChar.ToString();
        return leftFull.StartsWith(rightFull + separator, comparison)
            || rightFull.StartsWith(leftFull + separator, comparison);
    }

    private static void ValidateRecipe(ExternalCardImportRecipe recipe)
    {
        if (recipe.FormatVersion != 1)
            throw new InvalidDataException($"Неподдерживаемая версия рецепта: {recipe.FormatVersion}.");
        if (recipe.Cards.Count == 0)
            throw new InvalidDataException("Рецепт не содержит ни одной карты.");
        if (!recipe.PreserveCatalogTopLevelCrc)
            throw new InvalidDataException(
                "Пересчёт верхнего CRC каталогов пока не реализован. " +
                "Оставьте PreserveCatalogTopLevelCrc=true.");

        var targetIds = new HashSet<int>();
        foreach (ExternalCardImportEntry card in recipe.Cards)
        {
            if (card.SourceTemplateId <= 0)
                throw new InvalidDataException("SourceTemplateId должен быть положительным.");
            if (card.TargetTemplateId <= 0)
                card.TargetTemplateId = card.SourceTemplateId;
            if (!targetIds.Add(card.TargetTemplateId))
                throw new InvalidDataException($"TargetTemplateId повторяется в рецепте: {card.TargetTemplateId}.");

            string abilityMode = NormalizeAbilityMode(card.AbilityMode);
            if (abilityMode == "xmlfile" && string.IsNullOrWhiteSpace(card.AbilityXmlFile))
                throw new InvalidDataException(
                    $"Для карты {card.TargetTemplateId} AbilityMode=XmlFile требует AbilityXmlFile.");
            if (abilityMode == "clonetarget" && !card.CloneTargetAbilityFromCardId.HasValue)
                throw new InvalidDataException(
                    $"Для карты {card.TargetTemplateId} AbilityMode=CloneTarget требует CloneTargetAbilityFromCardId.");
            _ = NormalizeLocalizationConflictPolicy(card.LocalizationConflictPolicy);
        }
    }

    private static void BuildPlanAndOptionallyMutate(ImportContext context, bool mutate)
    {
        foreach (ExternalCardImportEntry entry in context.Recipe.Cards)
            ProcessCard(context, entry, mutate);

        context.Preview.FilesToModify.Add("data_definitions");
        if (context.HasAudioChanges)
        {
            context.Preview.FilesToModify.Add("audio.json");
            context.Preview.FilesToModify.Add("Audio/event_inclusion.json");
            context.Preview.FilesToModify.Add("Audio/soundbank_inclusion.json");
        }
        if (context.HasArtChanges)
            context.Preview.FilesToModify.Add("bundledassets.json");

        context.Preview.FilesToModify.Sort(StringComparer.OrdinalIgnoreCase);
        context.Preview.ResourcesToCopy.Sort(StringComparer.OrdinalIgnoreCase);
        context.Preview.Changes.Sort(StringComparer.OrdinalIgnoreCase);

        if (mutate)
        {
            SaveXml(context.TargetTemplates, Path.Combine(context.TargetWorkspace, "Templates.xml"));
            SaveXml(context.TargetAbilities, Path.Combine(context.TargetWorkspace, "Abilities.xml"));
            if (context.HasArtChanges)
                SaveXml(context.TargetArtDefinitions, Path.Combine(context.TargetWorkspace, "ArtDefinitions.xml"));
            if (context.HasAudioChanges)
                SaveXml(context.TargetCardAudio, Path.Combine(context.TargetWorkspace, "CardAudio.xml"));
        }

        if (context.HasArtChanges)
        {
            context.Preview.Warnings.Add(
                "bundledassets_build и bundledassets_build.manifest не изменяются. " +
                "Для проверенного импорта Rogue Mage это работает, но некоторые beta/premium bundles " +
                "могут потребовать пересборку master manifest.");
        }
        if (context.Recipe.PreserveCatalogTopLevelCrc)
        {
            context.Preview.Warnings.Add(
                "Верхние CRC полей audio.json/bundledassets.json сохраняются. " +
                "Индивидуальные CRC, размеры и пути копируются из исходного каталога.");
        }
    }

    private static void ProcessCard(
        ImportContext context,
        ExternalCardImportEntry entry,
        bool mutate)
    {
        XElement sourceTemplate = FindTemplate(context.SourceTemplates, entry.SourceTemplateId)
            ?? throw new InvalidDataException(
                $"В исходном data_definitions не найден CardTemplate {entry.SourceTemplateId}.");

        int sourceArtId = ReadRequiredIntAttribute(sourceTemplate, "ArtId");
        int sourceAudioId = ReadRequiredIntAttribute(sourceTemplate, "AudioId");
        int targetArtId = entry.TargetArtId ?? sourceArtId;
        int targetAudioId = entry.TargetAudioId ?? sourceAudioId;
        string debugName = string.IsNullOrWhiteSpace(entry.DebugName)
            ? (string?)sourceTemplate.Attribute("DebugName") ?? $"Imported {entry.TargetTemplateId}"
            : entry.DebugName.Trim();

        var cardPreview = new ExternalCardImportCardPreview
        {
            SourceTemplateId = entry.SourceTemplateId,
            TargetTemplateId = entry.TargetTemplateId,
            DebugName = debugName,
            SourceArtId = sourceArtId,
            TargetArtId = targetArtId,
            SourceAudioId = sourceAudioId,
            TargetAudioId = targetAudioId,
            AbilityMode = entry.AbilityMode
        };
        context.Preview.Cards.Add(cardPreview);

        XElement? existingTemplate = FindTemplate(context.TargetTemplates, entry.TargetTemplateId);
        if (existingTemplate != null && !entry.ReplaceExistingTemplate)
        {
            throw new InvalidDataException(
                $"Target TemplateId {entry.TargetTemplateId} уже существует. " +
                "Укажите ReplaceExistingTemplate=true только при осознанной замене.");
        }

        XElement targetTemplate = new(sourceTemplate);
        targetTemplate.SetAttributeValue("Id", entry.TargetTemplateId);
        targetTemplate.SetAttributeValue("DebugName", debugName);
        targetTemplate.SetAttributeValue("ArtId", targetArtId);
        targetTemplate.SetAttributeValue("AudioId", targetAudioId);
        if (entry.Availability.HasValue)
            targetTemplate.SetAttributeValue("Availability", entry.Availability.Value);
        SetElementValueIfSpecified(targetTemplate, "Provision", entry.Provision);
        SetElementValueIfSpecified(targetTemplate, "Rarity", entry.Rarity);
        SetElementValueIfSpecified(targetTemplate, "Tier", entry.Tier);
        SetElementValueIfSpecified(targetTemplate, "Type", entry.Type);
        SetElementValueIfSpecified(targetTemplate, "FactionId", entry.FactionId);
        SetElementValueIfSpecified(targetTemplate, "SecondaryFactionId", entry.SecondaryFactionId);
        foreach ((string path, string value) in entry.TemplateOverrides)
            ApplyTemplateOverride(targetTemplate, path, value);

        if (entry.ReplaceAbilityIds)
        {
            XElement abilityIds = targetTemplate.Element("AbilityIds") ?? new XElement("AbilityIds");
            if (abilityIds.Parent == null)
            {
                XElement? linkedOrder = targetTemplate.Element("LinkedTemplateOrder");
                if (linkedOrder != null)
                    linkedOrder.AddAfterSelf(abilityIds);
                else
                    targetTemplate.Add(abilityIds);
            }
            abilityIds.RemoveNodes();
            abilityIds.Add(new XElement("e0", entry.TargetTemplateId));
        }

        if (mutate)
        {
            existingTemplate?.Remove();
            context.TargetTemplates.Root!.Add(targetTemplate);
        }
        context.Preview.Changes.Add(
            $"CardTemplate {entry.SourceTemplateId} -> {entry.TargetTemplateId} ({debugName}).");

        ProcessAbility(context, entry, mutate);
        ProcessLocalization(context, entry, debugName, mutate);

        if (entry.ImportArt)
            ProcessArt(context, entry, sourceArtId, targetArtId, cardPreview, mutate);
        else
            ValidateExistingTargetArt(context, targetArtId);

        if (entry.ImportAudio && sourceAudioId != 0)
        {
            ProcessAudio(context, entry, sourceAudioId, targetAudioId, debugName, cardPreview, mutate);
        }
        else if (entry.ImportAudio)
        {
            if (targetAudioId != 0)
            {
                throw new InvalidDataException(
                    $"Source AudioId=0, но TargetAudioId={targetAudioId}. " +
                    "Укажите ImportAudio=false для использования существующего целевого звука.");
            }
            context.Preview.Warnings.Add(
                $"Карта {entry.TargetTemplateId}: Source AudioId=0, аудиоресурсы не импортируются.");
        }
        else if (targetAudioId != 0)
        {
            ValidateExistingTargetAudio(context, targetAudioId);
        }
    }

    private static void ProcessAbility(
        ImportContext context,
        ExternalCardImportEntry entry,
        bool mutate)
    {
        string mode = NormalizeAbilityMode(entry.AbilityMode);
        XElement? existing = FindCardAbility(context.TargetAbilities, entry.TargetTemplateId);

        if (mode == "existingtarget")
        {
            if (existing == null)
                throw new InvalidDataException(
                    $"AbilityMode=ExistingTarget, но CardAbility {entry.TargetTemplateId} отсутствует.");
            context.Preview.Changes.Add($"CardAbility {entry.TargetTemplateId}: сохранена существующая.");
            return;
        }

        if (mode == "none")
        {
            if (existing != null && entry.ReplaceExistingAbility && mutate)
                existing.Remove();
            context.Preview.Warnings.Add(
                $"Карта {entry.TargetTemplateId}: AbilityMode=None. Проверьте AbilityIds в шаблоне.");
            return;
        }

        XElement ability;
        if (mode == "xmlfile")
        {
            string path = ResolveRecipeRelativeFile(context.RecipePath, entry.AbilityXmlFile);
            XDocument document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            ability = document.Root?.Name.LocalName == "Ability"
                ? new XElement(document.Root)
                : new XElement(document.Descendants("Ability").FirstOrDefault()
                    ?? throw new InvalidDataException($"В {path} не найден элемент Ability."));
        }
        else if (mode == "clonetarget")
        {
            ability = new XElement(
                FindCardAbility(context.TargetAbilities, entry.CloneTargetAbilityFromCardId!.Value)
                ?? throw new InvalidDataException(
                    $"Не найдена целевая способность для клонирования: {entry.CloneTargetAbilityFromCardId}."));
        }
        else
        {
            throw new InvalidDataException($"Неизвестный AbilityMode: {entry.AbilityMode}.");
        }

        ability.SetAttributeValue("Type", "CardAbility");
        ability.SetAttributeValue("Id", entry.TargetTemplateId);

        int nId = entry.TargetAbilityNId
            ?? (mode == "clonetarget"
                ? context.GetNextAbilityNId()
                : (int?)ability.Attribute("NId") ?? context.GetNextAbilityNId());
        context.ReserveAbilityNId(nId, entry.TargetTemplateId);
        ability.SetAttributeValue("NId", nId);

        XElement? nIdConflict = context.TargetAbilities.Root?
            .Elements("Ability")
            .FirstOrDefault(candidate =>
                (int?)candidate.Attribute("NId") == nId
                && (int?)candidate.Attribute("Id") != entry.TargetTemplateId);
        if (nIdConflict != null)
        {
            throw new InvalidDataException(
                $"NId {nId} уже используется Ability Id={(int?)nIdConflict.Attribute("Id")}. " +
                "Укажите свободный TargetAbilityNId.");
        }

        if (existing != null && !entry.ReplaceExistingAbility)
        {
            if (!XmlEquivalent(existing, ability))
            {
                throw new InvalidDataException(
                    $"CardAbility {entry.TargetTemplateId} уже существует и отличается. " +
                    "Используйте AbilityMode=ExistingTarget либо ReplaceExistingAbility=true.");
            }
            context.Preview.Changes.Add($"CardAbility {entry.TargetTemplateId}: уже совпадает, сохранена.");
            return;
        }

        if (mutate)
        {
            existing?.Remove();
            context.TargetAbilities.Root!.Add(ability);
        }
        context.Preview.Changes.Add(
            $"CardAbility {entry.TargetTemplateId}: mode={entry.AbilityMode}, NId={nId}.");
    }

    private static void ProcessLocalization(
        ImportContext context,
        ExternalCardImportEntry entry,
        string debugName,
        bool mutate)
    {
        string sourceLocalizationDirectory = Path.Combine(context.SourceWorkspace, "Localization");
        string targetLocalizationDirectory = Path.Combine(context.TargetWorkspace, "Localization");
        if (!Directory.Exists(targetLocalizationDirectory))
            throw new DirectoryNotFoundException(
                $"В целевом data_definitions отсутствует Localization: {targetLocalizationDirectory}");

        Dictionary<string, Dictionary<string, string>> sourceByLanguage =
            LoadSourceLocalization(sourceLocalizationDirectory, entry.SourceTemplateId);

        string fallbackLanguage = NormalizeLanguage(entry.FallbackLanguage);
        string conflictPolicy = NormalizeLocalizationConflictPolicy(entry.LocalizationConflictPolicy);
        entry.Localization.TryGetValue(fallbackLanguage, out ExternalCardLocalizationRecipe? fallbackOverride);
        sourceByLanguage.TryGetValue(fallbackLanguage, out Dictionary<string, string>? fallbackSource);

        foreach (string targetCsv in Directory.EnumerateFiles(targetLocalizationDirectory, "*.csv"))
        {
            string language = NormalizeLanguage(Path.GetFileNameWithoutExtension(targetCsv));
            entry.Localization.TryGetValue(language, out ExternalCardLocalizationRecipe? overrideValues);
            sourceByLanguage.TryGetValue(language, out Dictionary<string, string>? sourceValues);

            string name = FirstNonEmpty(
                overrideValues?.Name,
                entry.CopySourceName ? GetValue(sourceValues, "name") : null,
                fallbackOverride?.Name,
                entry.CopySourceName ? GetValue(fallbackSource, "name") : null,
                debugName);
            string tooltip = FirstNonEmpty(
                overrideValues?.Tooltip,
                entry.CopySourceTooltip ? GetValue(sourceValues, "tooltip") : null,
                fallbackOverride?.Tooltip,
                entry.CopySourceTooltip ? GetValue(fallbackSource, "tooltip") : null,
                "");
            string fluff = FirstNonEmpty(
                overrideValues?.Fluff,
                entry.CopySourceFluff ? GetValue(sourceValues, "fluff") : null,
                fallbackOverride?.Fluff,
                entry.CopySourceFluff ? GetValue(fallbackSource, "fluff") : null,
                "");

            LocalizationUpdateSummary summary = UpsertLocalization(
                targetCsv,
                entry.TargetTemplateId,
                name,
                tooltip,
                fluff,
                conflictPolicy,
                mutate);
            RegisterLocalizationSummary(context, entry, language, targetCsv, summary);
        }

        string rootRussian = Path.Combine(context.TargetWorkspace, "ru-ru.csv");
        if (File.Exists(rootRussian))
        {
            string language = "ru-ru";
            entry.Localization.TryGetValue(language, out ExternalCardLocalizationRecipe? overrideValues);
            sourceByLanguage.TryGetValue(language, out Dictionary<string, string>? sourceValues);
            string name = FirstNonEmpty(
                overrideValues?.Name,
                entry.CopySourceName ? GetValue(sourceValues, "name") : null,
                debugName);
            string tooltip = FirstNonEmpty(
                overrideValues?.Tooltip,
                entry.CopySourceTooltip ? GetValue(sourceValues, "tooltip") : null,
                fallbackOverride?.Tooltip,
                "");
            string fluff = FirstNonEmpty(
                overrideValues?.Fluff,
                entry.CopySourceFluff ? GetValue(sourceValues, "fluff") : null,
                fallbackOverride?.Fluff,
                "");
            LocalizationUpdateSummary summary = UpsertLocalization(
                rootRussian,
                entry.TargetTemplateId,
                name,
                tooltip,
                fluff,
                conflictPolicy,
                mutate);
            RegisterLocalizationSummary(
                context,
                entry,
                "ru-ru (root)",
                rootRussian,
                summary);
        }

        context.Preview.Changes.Add(
            $"Localization {entry.SourceTemplateId}_* -> {entry.TargetTemplateId}_*: " +
            $"policy={entry.LocalizationConflictPolicy}.");
    }

    private static void RegisterLocalizationSummary(
        ImportContext context,
        ExternalCardImportEntry entry,
        string language,
        string path,
        LocalizationUpdateSummary summary)
    {
        if (summary.PreservedKeys.Count > 0)
        {
            context.Preview.Warnings.Add(
                $"Карта {entry.TargetTemplateId}, {language}: сохранены отличающиеся значения целевого клиента " +
                $"({string.Join(", ", summary.PreservedKeys)}), policy=PreserveTarget, файл={path}.");
        }
        if (summary.OverwrittenKeys.Count > 0)
        {
            context.Preview.Warnings.Add(
                $"Карта {entry.TargetTemplateId}, {language}: будут перезаписаны значения целевого клиента " +
                $"({string.Join(", ", summary.OverwrittenKeys)}), policy=OverwriteTarget, файл={path}.");
        }
    }

    private static void ProcessArt(
        ImportContext context,
        ExternalCardImportEntry entry,
        int sourceArtId,
        int targetArtId,
        ExternalCardImportCardPreview cardPreview,
        bool mutate)
    {
        if (sourceArtId != targetArtId)
        {
            throw new InvalidDataException(
                $"Raw AssetBundle нельзя безопасно перенумеровать: Source ArtId={sourceArtId}, " +
                $"Target ArtId={targetArtId}. Для перенумерации требуется пересборка Unity bundle.");
        }

        XElement sourceArt = FindArtDefinition(context.SourceArtDefinitions, sourceArtId)
            ?? throw new InvalidDataException($"Не найден ArtDefinition {sourceArtId} в исходной игре.");
        XElement? targetArt = FindArtDefinition(context.TargetArtDefinitions, targetArtId);
        if (targetArt != null && !XmlEquivalent(targetArt, sourceArt))
            throw new InvalidDataException($"Target ArtId {targetArtId} уже занят другим ArtDefinition.");

        if (mutate && targetArt == null)
            context.TargetArtDefinitions.Root!.Add(new XElement(sourceArt));

        IReadOnlyList<JsonObject> entries = FindArtCatalogEntries(
            context.SourceBundledAssets,
            sourceArtId,
            entry.ArtMode);
        if (entries.Count == 0)
        {
            throw new InvalidDataException(
                $"Для ArtId {sourceArtId} в исходном bundledassets.json не найдены ресурсы " +
                $"режима {entry.ArtMode}.");
        }

        var visitedDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonObject sourceEntry in entries)
        {
            string path = ReadRequiredJsonString(sourceEntry, "Path");
            RegisterImportedArtResource(context, sourceEntry, forceCopy: true);
            cardPreview.ArtResourcePaths.Add(path);
            if (entry.ImportArtDependencies)
            {
                RegisterArtDependencies(
                    context,
                    sourceEntry,
                    visitedDependencies,
                    ownerPath: path);
            }
        }

        context.HasArtChanges = true;
        context.Preview.Changes.Add(
            $"ArtId {sourceArtId}: ArtDefinition + {entries.Count} AssetBundle catalog entries.");
    }

    private static void ValidateExistingTargetArt(ImportContext context, int targetArtId)
    {
        if (FindArtDefinition(context.TargetArtDefinitions, targetArtId) == null)
            throw new InvalidDataException(
                $"ImportArt=false, но целевой ArtId {targetArtId} отсутствует в ArtDefinitions.xml.");
    }

    private static void RegisterImportedArtResource(
        ImportContext context,
        JsonObject sourceEntry,
        bool forceCopy)
    {
        string path = ReadRequiredJsonString(sourceEntry, "Path");
        JsonObject? existing = FindCatalogInfo(context.TargetBundledAssets, path);
        if (existing != null && !JsonNode.DeepEquals(existing, sourceEntry))
        {
            throw new InvalidDataException(
                $"Конфликт записи {path} в bundledassets.json. " +
                "Для общих shader/VFX dependencies используется целевая версия, " +
                "но основной импортируемый bundle должен совпадать либо отсутствовать.");
        }

        if (existing == null)
            GetRequiredArray(context.TargetBundledAssets, "Infos").Add(sourceEntry.DeepClone());

        bool targetExists = TryResolveCaseInsensitiveFile(
            context.TargetStreamingAssets,
            path,
            out _);
        if (forceCopy || !targetExists)
            context.RegisterResource(path, sourceEntry);
    }

    private static void RegisterArtDependencies(
        ImportContext context,
        JsonObject ownerEntry,
        HashSet<string> visited,
        string ownerPath)
    {
        if (ownerEntry["AllDependencies"] is not JsonArray dependencies)
            return;

        foreach (JsonNode? dependencyNode in dependencies)
        {
            string dependency = dependencyNode?.GetValue<string>()?.Trim() ?? "";
            if (dependency.Length == 0 || !visited.Add(dependency))
                continue;

            JsonObject? targetEntry = FindCatalogInfo(context.TargetBundledAssets, dependency);
            bool targetFileExists = TryResolveCaseInsensitiveFile(
                context.TargetStreamingAssets,
                dependency,
                out _);
            JsonObject? sourceEntry = FindCatalogInfo(context.SourceBundledAssets, dependency);

            if (targetEntry != null && targetFileExists)
            {
                if (sourceEntry != null && !JsonNode.DeepEquals(targetEntry, sourceEntry))
                {
                    context.Preview.Warnings.Add(
                        $"Dependency {dependency} для {ownerPath} отличается между играми. " +
                        "Сохранена версия текущего GWENT; совместимость premium/VFX нужно проверить в игре.");
                }
                continue;
            }

            if (sourceEntry == null)
            {
                throw new InvalidDataException(
                    $"Dependency {dependency} для {ownerPath} отсутствует в source bundledassets.json.");
            }

            if (targetEntry != null && !JsonNode.DeepEquals(targetEntry, sourceEntry))
            {
                throw new InvalidDataException(
                    $"Dependency {dependency} зарегистрирован в target, но физический файл отсутствует " +
                    "и source catalog entry отличается. Автоматическая замена общего dependency небезопасна.");
            }

            RegisterImportedArtResource(context, sourceEntry, forceCopy: true);
            RegisterArtDependencies(context, sourceEntry, visited, dependency);
        }
    }

    private static void ProcessAudio(
        ImportContext context,
        ExternalCardImportEntry entry,
        int sourceAudioId,
        int targetAudioId,
        string debugName,
        ExternalCardImportCardPreview cardPreview,
        bool mutate)
    {
        XElement sourceOuter = FindCardAudioOuter(context.SourceCardAudio, sourceAudioId)
            ?? throw new InvalidDataException($"Не найден CardAudio {sourceAudioId} в исходной игре.");
        XElement sourceInner = sourceOuter
            .DescendantsAndSelf("CardAudio")
            .First(element => (int?)element.Attribute("id") == sourceAudioId);

        List<string> voiceoverKeys = sourceInner
            .Descendants("Voiceover")
            .Select(element => ((string?)element.Attribute("key") ?? "").Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (voiceoverKeys.Count > 0)
        {
            if (!entry.IgnoreVoiceovers)
            {
                throw new InvalidDataException(
                    $"AudioId {sourceAudioId} содержит {voiceoverKeys.Count} voiceover keys. " +
                    "Общий voicedata_<language>/vo AssetBundle нельзя безопасно слить без Unity-пересборки. " +
                    "Для импорта только SFX укажите IgnoreVoiceovers=true.");
            }
            context.Preview.Warnings.Add(
                $"AudioId {sourceAudioId}: {voiceoverKeys.Count} voiceover keys не переносятся; " +
                "импортируются только SoundEffect events.");
        }

        XElement importedOuter = new(sourceOuter);
        XElement importedInner = importedOuter
            .DescendantsAndSelf("CardAudio")
            .First(element => (int?)element.Attribute("id") == sourceAudioId);
        importedInner.SetAttributeValue("id", targetAudioId);
        string importedAudioName = ((string?)sourceInner.Attribute("name"))?.Trim() ?? "";
        importedInner.SetAttributeValue(
            "name",
            importedAudioName.Length > 0 ? importedAudioName : debugName);
        if (voiceoverKeys.Count > 0 && entry.IgnoreVoiceovers)
        {
            foreach (XElement trigger in importedInner.Descendants("Trigger"))
                trigger.SetAttributeValue("vo", "False");
            foreach (XElement voiceover in importedInner.Descendants("Voiceover").ToList())
                voiceover.Remove();
        }

        XElement? targetOuter = FindCardAudioOuter(context.TargetCardAudio, targetAudioId);
        if (targetOuter != null && !XmlEquivalent(targetOuter, importedOuter))
            throw new InvalidDataException($"Target AudioId {targetAudioId} уже занят другим CardAudio.");
        if (mutate && targetOuter == null)
            context.TargetCardAudio.Root!.Add(importedOuter);

        var eventIds = sourceInner
            .Descendants("SoundEffect")
            .Select(element => ParseWwiseEventId((string?)element.Attribute("event")))
            .Distinct()
            .ToList();
        if (eventIds.Count == 0)
            context.Preview.Warnings.Add($"AudioId {sourceAudioId} не содержит SoundEffect events.");

        foreach (uint eventId in eventIds)
        {
            string eventText = eventId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            IReadOnlyList<string> banks = FindBanksForEvent(context.SourceEventInclusion, eventText);
            if (banks.Count == 0)
                throw new InvalidDataException($"Event {eventText} не найден в source event_inclusion.json.");

            cardPreview.AudioEvents.Add(eventText);
            foreach (string bank in banks)
            {
                MergeMappingValue(context.TargetEventInclusion, bank, eventText);
                if (!cardPreview.SoundBanks.Contains(bank, StringComparer.OrdinalIgnoreCase))
                    cardPreview.SoundBanks.Add(bank);

                IReadOnlyList<string> packages = FindPackagesForBank(context.SourceSoundbankInclusion, bank);
                if (packages.Count == 0)
                    throw new InvalidDataException(
                        $"SoundBank {bank} не найден в source soundbank_inclusion.json.");

                foreach (string package in packages)
                {
                    MergeMappingValue(context.TargetSoundbankInclusion, package, bank);
                    if (!cardPreview.AudioPackages.Contains(package, StringComparer.OrdinalIgnoreCase))
                        cardPreview.AudioPackages.Add(package);

                    JsonObject sourceAudioInfo = FindCatalogInfo(
                        context.SourceAudioCatalog,
                        $"audio/{package}")
                        ?? throw new InvalidDataException(
                            $"В source audio.json нет записи audio/{package}.");
                    MergeCatalogInfo(context.TargetAudioCatalog, sourceAudioInfo, "audio.json");
                    context.RegisterResource(ReadRequiredJsonString(sourceAudioInfo, "Path"), sourceAudioInfo);
                }
            }
        }

        context.HasAudioChanges = true;
        context.Preview.Changes.Add(
            $"AudioId {sourceAudioId} -> {targetAudioId}: events={eventIds.Count}, " +
            $"banks={cardPreview.SoundBanks.Count}, packages={cardPreview.AudioPackages.Count}.");
    }

    private static void ValidateExistingTargetAudio(ImportContext context, int targetAudioId)
    {
        if (FindCardAudioOuter(context.TargetCardAudio, targetAudioId) == null)
            throw new InvalidDataException(
                $"ImportAudio=false, но целевой AudioId {targetAudioId} отсутствует в CardAudio.xml.");
    }

    private static void SaveModifiedCatalogs(ImportContext context, string payload)
    {
        if (context.HasAudioChanges)
        {
            SaveJsonNode(context.TargetAudioCatalog, Path.Combine(payload, "audio.json"));
            SaveJsonNode(
                context.TargetEventInclusion,
                Path.Combine(payload, "Audio", "event_inclusion.json"));
            SaveJsonNode(
                context.TargetSoundbankInclusion,
                Path.Combine(payload, "Audio", "soundbank_inclusion.json"));
            context.RegisterPayloadFile("audio.json", Path.Combine(payload, "audio.json"));
            context.RegisterPayloadFile(
                "Audio/event_inclusion.json",
                Path.Combine(payload, "Audio", "event_inclusion.json"));
            context.RegisterPayloadFile(
                "Audio/soundbank_inclusion.json",
                Path.Combine(payload, "Audio", "soundbank_inclusion.json"));
        }

        if (context.HasArtChanges)
        {
            SaveJsonNode(context.TargetBundledAssets, Path.Combine(payload, "bundledassets.json"));
            context.RegisterPayloadFile(
                "bundledassets.json",
                Path.Combine(payload, "bundledassets.json"));
        }
    }

    private static void CopyResources(ImportContext context, string payload)
    {
        foreach (ResourcePlan resource in context.Resources.Values
            .OrderBy(value => value.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            string source = ResolveCaseInsensitivePath(context.SourceStreamingAssets, resource.RelativePath);
            ValidatePhysicalResource(source, resource.CatalogInfo);
            string destination = Path.Combine(
                payload,
                resource.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            context.RegisterPayloadFile(resource.RelativePath, destination);

            string manifestSource = source + ".manifest";
            if (File.Exists(manifestSource))
            {
                string manifestRelative = resource.RelativePath + ".manifest";
                string manifestDestination = destination + ".manifest";
                File.Copy(manifestSource, manifestDestination, overwrite: true);
                context.RegisterPayloadFile(manifestRelative, manifestDestination);
            }
        }
    }

    private static void WriteReport(ImportContext context, string path)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# External card import: {context.Recipe.Name}");
        builder.AppendLine();
        builder.AppendLine($"- Source game: `{context.Recipe.SourceGame}`");
        builder.AppendLine($"- Source StreamingAssets: `{context.SourceStreamingAssets}`");
        builder.AppendLine($"- Target StreamingAssets: `{context.TargetStreamingAssets}`");
        builder.AppendLine($"- Target base data_definitions SHA-256: `{context.BaseFileStates["data_definitions"]}`");
        builder.AppendLine();
        builder.AppendLine("## Cards");
        builder.AppendLine();
        foreach (ExternalCardImportCardPreview card in context.Preview.Cards)
        {
            builder.AppendLine($"### {card.TargetTemplateId} — {card.DebugName}");
            builder.AppendLine();
            builder.AppendLine($"- Source TemplateId: `{card.SourceTemplateId}`");
            builder.AppendLine($"- ArtId: `{card.SourceArtId}` → `{card.TargetArtId}`");
            builder.AppendLine($"- AudioId: `{card.SourceAudioId}` → `{card.TargetAudioId}`");
            builder.AppendLine($"- Ability mode: `{card.AbilityMode}`");
            if (card.ArtResourcePaths.Count > 0)
                builder.AppendLine($"- Art resources: {card.ArtResourcePaths.Count}");
            if (card.AudioEvents.Count > 0)
                builder.AppendLine($"- Audio events: `{string.Join("`, `", card.AudioEvents)}`");
            if (card.SoundBanks.Count > 0)
                builder.AppendLine($"- SoundBanks: `{string.Join("`, `", card.SoundBanks)}`");
            if (card.AudioPackages.Count > 0)
                builder.AppendLine($"- PCK: `{string.Join("`, `", card.AudioPackages)}`");
            builder.AppendLine();
        }

        builder.AppendLine("## Changes");
        builder.AppendLine();
        foreach (string change in context.Preview.Changes)
            builder.AppendLine($"- {change}");
        builder.AppendLine();
        builder.AppendLine("## Payload");
        builder.AppendLine();
        foreach (string relative in context.PayloadFiles.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            builder.AppendLine($"- `{relative}`");
        builder.AppendLine();
        builder.AppendLine("## Warnings");
        builder.AppendLine();
        foreach (string warning in context.Preview.Warnings)
            builder.AppendLine($"- {warning}");
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void WriteManifest(ImportContext context, string path)
    {
        var manifest = new
        {
            formatVersion = 1,
            name = context.Recipe.Name,
            sourceGame = context.Recipe.SourceGame,
            sourceStreamingAssets = context.SourceStreamingAssets,
            targetStreamingAssets = context.TargetStreamingAssets,
            baseFileStates = context.BaseFileStates,
            payloadFiles = context.PayloadFiles,
            resources = context.Resources.Values.Select(resource => new
            {
                path = resource.RelativePath,
                catalog = resource.CatalogInfo
            }),
            cards = context.Preview.Cards,
            warnings = context.Preview.Warnings
        };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static void WriteInstallerScripts(
        ImportContext context,
        string outputDirectory,
        string installPath,
        string uninstallPath)
    {
        string baseChecks = string.Join(
            Environment.NewLine,
            context.BaseFileStates
                .Where(pair => pair.Value != null)
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair =>
                    $"    [PSCustomObject]@{{ Relative = '{EscapePowerShell(pair.Key)}'; Sha256 = '{EscapePowerShell(pair.Value!)}' }}"));
        string payloadFiles = string.Join(
            Environment.NewLine,
            context.PayloadFiles.Keys
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Select(value => $"    '{EscapePowerShell(value)}'"));
        string payloadChecks = string.Join(
            Environment.NewLine,
            context.PayloadFiles
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair =>
                    $"    [PSCustomObject]@{{ Relative = '{EscapePowerShell(pair.Key)}'; Sha256 = '{EscapePowerShell(pair.Value.Sha256)}' }}"));
        string expectedAbsent = string.Join(
            Environment.NewLine,
            context.BaseFileStates
                .Where(pair => pair.Value == null)
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"    '{EscapePowerShell(pair.Key)}'"));

        string install = $$"""
param(
    [Parameter(Mandatory=$true, Position=0)]
    [ValidateNotNullOrEmpty()]
    [string]$StreamingAssets
)

$ErrorActionPreference = 'Stop'

function Get-Sha256([string]$Path) {
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Copy-WithParents([string]$Source, [string]$Destination) {
    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

$root = (Resolve-Path -LiteralPath $StreamingAssets -ErrorAction Stop).Path
if ((Split-Path -Leaf $root) -ne 'StreamingAssets') {
    throw "Передана не папка StreamingAssets: $root"
}

$checks = @(
{{baseChecks}}
)
foreach ($check in $checks) {
    $relative = $check.Relative
    $expected = $check.Sha256
    $target = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "Не найден целевой файл: $target"
    }
    $actual = Get-Sha256 $target
    if ($actual -ne $expected) {
        throw "Файл отличается от базы, для которой собран импорт: $target`nОжидался: $expected`nПолучен: $actual"
    }
}

$expectedAbsent = @(
{{expectedAbsent}}
)
foreach ($relative in $expectedAbsent) {
    $target = Join-Path $root $relative
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        throw "Путь должен отсутствовать в базовой установке, но уже существует: $target"
    }
}

$payloadChecks = @(
{{payloadChecks}}
)
foreach ($check in $payloadChecks) {
    $relative = $check.Relative
    $expected = $check.Sha256
    $source = Join-Path $PSScriptRoot ('payload\' + $relative)
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "В payload отсутствует файл: $source"
    }
    $actual = Get-Sha256 $source
    if ($actual -ne $expected) {
        throw "Повреждён payload: $source`nОжидался SHA256: $expected`nПолучен: $actual"
    }
}

$payloadFiles = @(
{{payloadFiles}}
)
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backup = Join-Path $root ('_ExternalCardImporter_Backup\' + $stamp)
New-Item -ItemType Directory -Force -Path $backup | Out-Null
$absent = New-Object 'System.Collections.Generic.List[string]'

foreach ($relative in $payloadFiles) {
    $target = Join-Path $root $relative
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        Copy-WithParents $target (Join-Path $backup $relative)
    }
    else {
        $absent.Add($relative)
    }
}
$absent | Set-Content -LiteralPath (Join-Path $backup 'ABSENT_FILES.txt') -Encoding UTF8

try {
    foreach ($relative in $payloadFiles) {
        $source = Join-Path $PSScriptRoot ('payload\' + $relative)
        Copy-WithParents $source (Join-Path $root $relative)
    }
}
catch {
    Write-Warning 'Установка прервана. Выполняется автоматический rollback.'
    foreach ($relative in $payloadFiles) {
        $backupFile = Join-Path $backup $relative
        $target = Join-Path $root $relative
        if (Test-Path -LiteralPath $backupFile -PathType Leaf) {
            Copy-WithParents $backupFile $target
        }
        elseif (Test-Path -LiteralPath $target -PathType Leaf) {
            Remove-Item -LiteralPath $target -Force
        }
    }
    throw
}

Set-Content -LiteralPath (Join-Path $PSScriptRoot 'LAST_BACKUP.txt') -Value $backup -Encoding UTF8
Write-Host ''
Write-Host 'Импорт установлен.'
Write-Host "Backup: $backup"
""";

        string uninstall = """
param(
    [Parameter(Mandatory=$true, Position=0)]
    [ValidateNotNullOrEmpty()]
    [string]$StreamingAssets,

    [Parameter(Position=1)]
    [string]$BackupDirectory = ''
)

$ErrorActionPreference = 'Stop'

function Copy-WithParents([string]$Source, [string]$Destination) {
    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

$root = (Resolve-Path -LiteralPath $StreamingAssets -ErrorAction Stop).Path
if ([string]::IsNullOrWhiteSpace($BackupDirectory)) {
    $last = Join-Path $PSScriptRoot 'LAST_BACKUP.txt'
    if (-not (Test-Path -LiteralPath $last -PathType Leaf)) {
        throw 'Не указан BackupDirectory и отсутствует LAST_BACKUP.txt.'
    }
    $BackupDirectory = (Get-Content -LiteralPath $last -Raw).Trim()
}
$backup = (Resolve-Path -LiteralPath $BackupDirectory -ErrorAction Stop).Path

Get-ChildItem -LiteralPath $backup -File -Recurse | ForEach-Object {
    if ($_.Name -ne 'ABSENT_FILES.txt') {
        $relative = $_.FullName.Substring($backup.Length).TrimStart('\')
        Copy-WithParents $_.FullName (Join-Path $root $relative)
    }
}

$absentFile = Join-Path $backup 'ABSENT_FILES.txt'
if (Test-Path -LiteralPath $absentFile -PathType Leaf) {
    Get-Content -LiteralPath $absentFile | ForEach-Object {
        $relative = $_.Trim()
        if (-not [string]::IsNullOrWhiteSpace($relative)) {
            $target = Join-Path $root $relative
            if (Test-Path -LiteralPath $target -PathType Leaf) {
                Remove-Item -LiteralPath $target -Force
            }
        }
    }
}

Write-Host ''
Write-Host "Исходное состояние восстановлено из: $backup"
""";

        File.WriteAllText(installPath, install, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(uninstallPath, uninstall, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static void WriteVerifyScript(ImportContext context, string path)
    {
        string checks = string.Join(
            Environment.NewLine,
            context.BaseFileStates
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Value == null
                    ? $"    [PSCustomObject]@{{ Relative = '{EscapePowerShell(pair.Key)}'; Sha256 = '' }}"
                    : $"    [PSCustomObject]@{{ Relative = '{EscapePowerShell(pair.Key)}'; Sha256 = '{EscapePowerShell(pair.Value)}' }}"));
        string script = $$"""
param(
    [Parameter(Mandatory=$true, Position=0)]
    [ValidateNotNullOrEmpty()]
    [string]$StreamingAssets
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $StreamingAssets -ErrorAction Stop).Path
$checks = @(
{{checks}}
)
$ok = $true
foreach ($check in $checks) {
    $relative = $check.Relative
    $expected = $check.Sha256
    $target = Join-Path $root $relative
    if ([string]::IsNullOrWhiteSpace($expected)) {
        $state = if (Test-Path -LiteralPath $target -PathType Leaf) { 'UNEXPECTED' } else { 'ABSENT OK' }
        if ($state -eq 'UNEXPECTED') { $ok = $false }
        Write-Host "$state`t$relative"
    }
    elseif (-not (Test-Path -LiteralPath $target -PathType Leaf)) {
        $ok = $false
        Write-Host "MISSING`t$relative"
    }
    else {
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash.ToLowerInvariant()
        $state = if ($actual -eq $expected) { 'BASE OK' } else { 'DIFFERENT' }
        if ($state -eq 'DIFFERENT') { $ok = $false }
        Write-Host "$state`t$relative`t$actual"
    }
}
if (-not $ok) { exit 1 }
""";
        File.WriteAllText(path, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static void WriteCmdWrapper(string output, string fileName, string scriptName)
    {
        string text = $"""
@echo off
setlocal
if "%~1"=="" (
    echo ERROR: StreamingAssets path was not provided.
    echo Example: %~nx0 "G:\Games\Gwent\Gwent_Data\StreamingAssets"
    pause
    exit /b 2
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0{scriptName}" -StreamingAssets "%~1"
set "EXITCODE=%ERRORLEVEL%"
echo.
if not "%EXITCODE%"=="0" (echo Operation failed. Exit code: %EXITCODE%) else (echo Operation completed successfully.)
pause
exit /b %EXITCODE%
""";
        File.WriteAllText(Path.Combine(output, fileName), text.Replace("\n", "\r\n"), Encoding.ASCII);
    }

    private static IReadOnlyList<JsonObject> FindArtCatalogEntries(
        JsonObject catalog,
        int artId,
        string artMode)
    {
        string normalizedMode = string.IsNullOrWhiteSpace(artMode)
            ? "standardonly"
            : artMode.Trim().ToLowerInvariant();
        if (normalizedMode is not ("standardonly" or "allavailable"))
            throw new InvalidDataException($"Неизвестный ArtMode: {artMode}.");

        var result = new List<JsonObject>();
        foreach (JsonObject entry in GetInfoObjects(catalog))
        {
            string path = ReadRequiredJsonString(entry, "Path").Replace('\\', '/');
            string fileName = path.Split('/').Last();
            if (!long.TryParse(fileName, out long assetId))
                continue;
            if (assetId / 10000 != artId)
                continue;
            if (normalizedMode == "standardonly"
                && !path.Contains("/textures/standard/", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("/slotart/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            result.Add(entry);
        }
        return result;
    }

    private static IReadOnlyList<string> FindBanksForEvent(JsonObject root, string eventId)
    {
        JsonObject mapping = GetRequiredObject(root, "InclusionMapping");
        return mapping
            .Where(pair => pair.Value is JsonArray array
                && array.Any(node => string.Equals(node?.GetValue<string>(), eventId, StringComparison.Ordinal)))
            .Select(pair => pair.Key)
            .ToList();
    }

    private static IReadOnlyList<string> FindPackagesForBank(JsonObject root, string bank)
    {
        JsonObject mapping = GetRequiredObject(root, "InclusionMapping");
        return mapping
            .Where(pair => pair.Value is JsonArray array
                && array.Any(node => string.Equals(node?.GetValue<string>(), bank, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => pair.Key)
            .ToList();
    }

    private static void MergeMappingValue(JsonObject root, string key, string value)
    {
        JsonObject mapping = GetRequiredObject(root, "InclusionMapping");
        JsonArray array;
        if (mapping.TryGetPropertyValue(key, out JsonNode? existing) && existing is JsonArray existingArray)
            array = existingArray;
        else
        {
            array = new JsonArray();
            mapping[key] = array;
        }

        if (!array.Any(node => string.Equals(node?.GetValue<string>(), value, StringComparison.OrdinalIgnoreCase)))
            array.Add(value);
    }

    private static void MergeCatalogInfo(JsonObject target, JsonObject sourceEntry, string catalogName)
    {
        string path = ReadRequiredJsonString(sourceEntry, "Path");
        JsonObject? existing = FindCatalogInfo(target, path);
        if (existing == null)
        {
            GetRequiredArray(target, "Infos").Add(sourceEntry.DeepClone());
            return;
        }

        if (!JsonNode.DeepEquals(existing, sourceEntry))
            throw new InvalidDataException($"Конфликт записи {path} в {catalogName}.");
    }

    private static JsonObject? FindCatalogInfo(JsonObject root, string path) =>
        GetInfoObjects(root).FirstOrDefault(entry =>
            string.Equals(ReadRequiredJsonString(entry, "Path"), path, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<JsonObject> GetInfoObjects(JsonObject root) =>
        GetRequiredArray(root, "Infos").OfType<JsonObject>();

    private static JsonArray GetRequiredArray(JsonObject root, string name) =>
        root[name] as JsonArray
        ?? throw new InvalidDataException($"JSON не содержит массива {name}.");

    private static JsonObject GetRequiredObject(JsonObject root, string name) =>
        root[name] as JsonObject
        ?? throw new InvalidDataException($"JSON не содержит объекта {name}.");

    private static string ReadRequiredJsonString(JsonObject root, string name) =>
        root[name]?.GetValue<string>()
        ?? throw new InvalidDataException($"JSON entry не содержит строку {name}.");

    private static bool XmlEquivalent(XElement left, XElement right) =>
        string.Equals(
            CanonicalizeXml(left).ToString(SaveOptions.DisableFormatting),
            CanonicalizeXml(right).ToString(SaveOptions.DisableFormatting),
            StringComparison.Ordinal);

    private static XElement CanonicalizeXml(XElement source)
    {
        var result = new XElement(
            source.Name,
            source.Attributes()
                .OrderBy(attribute => attribute.Name.NamespaceName, StringComparer.Ordinal)
                .ThenBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal)
                .Select(attribute => new XAttribute(attribute.Name, attribute.Value)));
        foreach (XNode node in source.Nodes())
        {
            if (node is XElement element)
                result.Add(CanonicalizeXml(element));
            else if (node is XText text && !string.IsNullOrWhiteSpace(text.Value))
                result.Add(new XText(text.Value));
            else if (node is XCData cdata)
                result.Add(new XCData(cdata.Value));
        }
        return result;
    }

    private static XElement? FindTemplate(XDocument document, int id) =>
        document.Root?.Elements("Template").FirstOrDefault(element =>
            string.Equals((string?)element.Attribute("Type"), "CardTemplate", StringComparison.Ordinal)
            && (int?)element.Attribute("Id") == id);

    private static XElement? FindCardAbility(XDocument document, int id) =>
        document.Root?.Elements("Ability").FirstOrDefault(element =>
            string.Equals((string?)element.Attribute("Type"), "CardAbility", StringComparison.Ordinal)
            && (int?)element.Attribute("Id") == id);

    private static XElement? FindArtDefinition(XDocument document, int artId) =>
        document.Root?.Elements("ArtDefinition")
            .FirstOrDefault(element => (int?)element.Attribute("ArtId") == artId);

    private static XElement? FindCardAudioOuter(XDocument document, int audioId)
    {
        XElement? inner = document.Descendants("CardAudio")
            .FirstOrDefault(element => (int?)element.Attribute("id") == audioId);
        if (inner == null)
            return null;
        if (inner.Parent?.Name.LocalName == "CardAudio"
            && inner.Parent.Attribute("Type") != null)
        {
            return inner.Parent;
        }
        return inner;
    }

    private static int ReadRequiredIntAttribute(XElement element, string name) =>
        (int?)element.Attribute(name)
        ?? throw new InvalidDataException($"Элемент {element.Name} не содержит {name}.");

    private static void ApplyTemplateOverride(XElement template, string path, string value)
    {
        string normalized = (path ?? "").Trim().Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new InvalidDataException("TemplateOverrides содержит пустой путь.");

        XElement current = template;
        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index].Trim();
            bool isLast = index == segments.Length - 1;
            if (segment.StartsWith('@'))
            {
                if (!isLast || segment.Length == 1)
                    throw new InvalidDataException($"Некорректный путь TemplateOverrides: {path}.");
                current.SetAttributeValue(segment[1..], value);
                return;
            }
            if (!IsSimpleXmlName(segment))
                throw new InvalidDataException($"Некорректный XML segment в TemplateOverrides: {segment}.");
            XElement? child = current.Element(segment);
            if (child == null)
            {
                child = new XElement(segment);
                current.Add(child);
            }
            if (isLast)
            {
                child.Value = value;
                return;
            }
            current = child;
        }
    }

    private static bool IsSimpleXmlName(string value)
    {
        if (value.Length == 0 || !(char.IsLetter(value[0]) || value[0] == '_'))
            return false;
        return value.All(character =>
            char.IsLetterOrDigit(character) || character is '_' or '-' or '.');
    }

    private static void SetElementValueIfSpecified(XElement template, string name, int? value)
    {
        if (!value.HasValue)
            return;
        XElement element = template.Element(name) ?? new XElement(name);
        if (element.Parent == null)
            template.Add(element);
        element.Value = value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static uint ParseWwiseEventId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("SoundEffect event пуст.");
        long value = long.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        if (value < int.MinValue || value > uint.MaxValue)
            throw new InvalidDataException($"Некорректный Wwise event id: {text}.");
        return value < 0
            ? unchecked((uint)(int)value)
            : checked((uint)value);
    }

    private static string NormalizeAbilityMode(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "xmlfile"
            : value.Trim().Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();

    private static string NormalizeLocalizationConflictPolicy(string? value)
    {
        string normalized = string.IsNullOrWhiteSpace(value)
            ? "requireexactmatch"
            : value.Trim()
                .Replace("_", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal)
                .ToLowerInvariant();
        return normalized switch
        {
            "requireexactmatch" or "exact" or "error" => "requireexactmatch",
            "preservetarget" or "preserve" or "keep" => "preservetarget",
            "overwritetarget" or "overwrite" or "replace" => "overwritetarget",
            _ => throw new InvalidDataException(
                $"Неизвестный LocalizationConflictPolicy: {value}. " +
                "Допустимы RequireExactMatch, PreserveTarget, OverwriteTarget.")
        };
    }

    private static string NormalizeLanguage(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "en-us" : value.Trim().ToLowerInvariant();

    private static Dictionary<string, Dictionary<string, string>> LoadSourceLocalization(
        string directory,
        int sourceTemplateId)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
            return result;
        foreach (string path in Directory.EnumerateFiles(directory, "*.csv"))
        {
            string language = NormalizeLanguage(Path.GetFileNameWithoutExtension(path));
            result[language] = ReadLocalizationValues(path, sourceTemplateId);
        }
        return result;
    }

    private static Dictionary<string, string> ReadLocalizationValues(string path, int id)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            int separator = line.IndexOf(';');
            if (separator < 0)
                continue;
            string key = line[..separator];
            string prefix = id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "_";
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            result[key[prefix.Length..]] = line[(separator + 1)..];
        }
        return result;
    }

    private static LocalizationUpdateSummary UpsertLocalization(
        string path,
        int targetId,
        string name,
        string tooltip,
        string fluff,
        string conflictPolicy,
        bool mutate)
    {
        string policy = NormalizeLocalizationConflictPolicy(conflictPolicy);
        byte[] bytes = File.ReadAllBytes(path);
        byte[] preamble = Encoding.UTF8.GetPreamble();
        bool bom = bytes.Length >= preamble.Length
            && bytes.AsSpan(0, preamble.Length).SequenceEqual(preamble);
        string text = File.ReadAllText(path, Encoding.UTF8);
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{targetId}_name"] = name,
            [$"{targetId}_tooltip"] = tooltip,
            [$"{targetId}_fluff"] = fluff
        };
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var output = new List<string>(lines.Length + 3);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var summary = new LocalizationUpdateSummary();
        foreach (string line in lines)
        {
            int separator = line.IndexOf(';');
            if (separator < 0)
            {
                output.Add(line);
                continue;
            }
            string key = line[..separator];
            if (!replacements.TryGetValue(key, out string? value))
            {
                output.Add(line);
                continue;
            }
            if (!seen.Add(key))
                throw new InvalidDataException($"Локализация {key} продублирована: {path}");

            string existingValue = line[(separator + 1)..];
            if (string.Equals(existingValue, value, StringComparison.Ordinal))
            {
                output.Add(line);
                summary.MatchedKeys.Add(key);
                continue;
            }

            switch (policy)
            {
                case "requireexactmatch":
                    throw new InvalidDataException(
                        $"Локализация {key} уже существует и отличается: {path}. " +
                        "Используйте LocalizationConflictPolicy=PreserveTarget, чтобы сохранить целевой текст, " +
                        "либо OverwriteTarget для явной замены.");
                case "preservetarget":
                    output.Add(line);
                    summary.PreservedKeys.Add(key);
                    break;
                case "overwritetarget":
                    output.Add(key + ";" + value);
                    summary.OverwrittenKeys.Add(key);
                    break;
                default:
                    throw new InvalidOperationException($"Необработанная политика локализации: {policy}.");
            }
        }
        while (output.Count > 0 && output[^1].Length == 0)
            output.RemoveAt(output.Count - 1);
        foreach ((string key, string value) in replacements)
        {
            if (seen.Contains(key))
                continue;
            output.Add(key + ";" + value);
            summary.AddedKeys.Add(key);
        }
        if (mutate)
        {
            string result = string.Join(newline, output) + newline;
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: bom);
            File.WriteAllText(path, result, encoding);
        }
        return summary;
    }

    private sealed class LocalizationUpdateSummary
    {
        public List<string> AddedKeys { get; } = new();
        public List<string> MatchedKeys { get; } = new();
        public List<string> PreservedKeys { get; } = new();
        public List<string> OverwrittenKeys { get; } = new();
    }

    private static string? GetValue(Dictionary<string, string>? values, string key) =>
        values != null && values.TryGetValue(key, out string? value) ? value : null;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";

    private static string ResolveRecipeRelativeFile(string recipePath, string input)
    {
        string value = input.Trim().Trim('"');
        if (Path.IsPathRooted(value))
            return Path.GetFullPath(value);
        string directory = Path.GetDirectoryName(Path.GetFullPath(recipePath))
            ?? Environment.CurrentDirectory;
        string candidate = Path.GetFullPath(Path.Combine(directory, value));
        if (!File.Exists(candidate))
            throw new FileNotFoundException("Файл рецепта способности не найден.", candidate);
        return candidate;
    }

    private static string ResolveStreamingAssets(string path, string label)
    {
        string full = Path.GetFullPath(path.Trim().Trim('"'));
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Не найдена папка StreamingAssets {label}: {full}");
        if (!File.Exists(Path.Combine(full, "data_definitions")))
            throw new InvalidDataException($"Папка не похожа на StreamingAssets {label}: {full}");
        return full;
    }

    private static string ResolveRequiredFile(string root, string relative) =>
        ResolveCaseInsensitivePath(root, relative);

    private static bool TryResolveCaseInsensitiveFile(
        string root,
        string relative,
        out string resolved)
    {
        try
        {
            resolved = ResolveCaseInsensitivePath(root, relative);
            return true;
        }
        catch (FileNotFoundException)
        {
            resolved = "";
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            resolved = "";
            return false;
        }
    }

    private static string ResolveCaseInsensitivePath(string root, string relative)
    {
        string normalized = relative.Replace('\\', '/').TrimStart('/');
        string current = root;
        foreach (string segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string direct = Path.Combine(current, segment);
            if (File.Exists(direct) || Directory.Exists(direct))
            {
                current = direct;
                continue;
            }
            string? match = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(candidate => string.Equals(
                    Path.GetFileName(candidate),
                    segment,
                    StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw new FileNotFoundException($"Не найден ресурс {relative} внутри {root}.");
            current = match;
        }
        if (!File.Exists(current))
            throw new FileNotFoundException($"Не найден файл {relative} внутри {root}.", current);
        return current;
    }

    private static void ValidatePhysicalResource(string path, JsonObject catalogInfo)
    {
        long size = catalogInfo["Size"]?.GetValue<long>() ?? 0;
        uint fileCrc = catalogInfo["FileCrc"]?.GetValue<uint>() ?? 0;
        var info = new FileInfo(path);
        if (size > 0 && info.Length != size)
            throw new InvalidDataException(
                $"Размер ресурса не совпадает с catalog: {path}, expected={size}, actual={info.Length}.");
        if (fileCrc != 0)
        {
            uint actual = ComputeCrc32(path);
            if (actual != fileCrc)
                throw new InvalidDataException(
                    $"CRC32 ресурса не совпадает: {path}, expected={fileCrc}, actual={actual}.");
        }
    }

    private static uint ComputeCrc32(string path)
    {
        uint crc = 0xFFFFFFFFu;
        byte[] buffer = new byte[1024 * 1024];
        using FileStream stream = File.OpenRead(path);
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int index = 0; index < count; index++)
            {
                crc ^= buffer[index];
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return ~crc;
    }

    private static void ExtractZip(string path, string destination)
    {
        Directory.CreateDirectory(destination);
        ZipFile.ExtractToDirectory(path, destination, overwriteFiles: true);
    }

    private static void RebuildZipPreservingEntries(string sourceZip, string workspace, string output)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        using ZipArchive source = ZipFile.OpenRead(sourceZip);
        using ZipArchive destination = ZipFile.Open(output, ZipArchiveMode.Create);
        foreach (ZipArchiveEntry sourceEntry in source.Entries)
        {
            ZipArchiveEntry targetEntry = destination.CreateEntry(
                sourceEntry.FullName,
                CompressionLevel.Optimal);
            targetEntry.LastWriteTime = sourceEntry.LastWriteTime;
            string workspaceFile = Path.Combine(
                workspace,
                sourceEntry.FullName.Replace('/', Path.DirectorySeparatorChar));
            using Stream outputStream = targetEntry.Open();
            if (File.Exists(workspaceFile))
            {
                using FileStream inputStream = File.OpenRead(workspaceFile);
                inputStream.CopyTo(outputStream);
            }
            else
            {
                using Stream inputStream = sourceEntry.Open();
                inputStream.CopyTo(outputStream);
            }
        }
    }

    private static void SaveXml(XDocument document, string path)
    {
        string xml = document.ToString(SaveOptions.DisableFormatting);
        File.WriteAllText(
            path,
            xml + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void SaveJsonNode(JsonObject node, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(JsonOptions) + Environment.NewLine, Encoding.UTF8);
    }

    private static JsonObject LoadJsonObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject
        ?? throw new InvalidDataException($"Не удалось прочитать JSON object: {path}");

    private static string ComputeSha256(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private sealed class ResourcePlan
    {
        public required string RelativePath { get; init; }
        public required JsonObject CatalogInfo { get; init; }
    }

    private sealed class PayloadFilePlan
    {
        public required long Size { get; init; }
        public required string Sha256 { get; init; }
    }

    private sealed class ImportContext : IDisposable
    {
        private readonly string _temporaryRoot;
        private readonly Dictionary<int, int> _reservedAbilityNIds = new();
        private int _nextAbilityNId = 1;

        public ImportContext(
            ExternalCardImportRecipe recipe,
            string recipePath,
            string sourceStreamingAssets,
            string targetStreamingAssets,
            string sourceDataDefinitionsPath,
            string targetDataDefinitionsPath)
        {
            Recipe = recipe;
            RecipePath = recipePath;
            SourceStreamingAssets = sourceStreamingAssets;
            TargetStreamingAssets = targetStreamingAssets;
            SourceDataDefinitionsPath = sourceDataDefinitionsPath;
            TargetDataDefinitionsPath = targetDataDefinitionsPath;
            _temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "AbilityEditor.ExternalImport." + Guid.NewGuid().ToString("N"));
            SourceWorkspace = Path.Combine(_temporaryRoot, "source");
            TargetWorkspace = Path.Combine(_temporaryRoot, "target");
            Preview = new ExternalCardImportPreview
            {
                RecipeName = recipe.Name,
                SourceStreamingAssets = sourceStreamingAssets,
                TargetStreamingAssets = targetStreamingAssets
            };
        }

        public ExternalCardImportRecipe Recipe { get; }
        public string RecipePath { get; }
        public string SourceStreamingAssets { get; }
        public string TargetStreamingAssets { get; }
        public string SourceDataDefinitionsPath { get; }
        public string TargetDataDefinitionsPath { get; }
        public string SourceWorkspace { get; }
        public string TargetWorkspace { get; }
        public ExternalCardImportPreview Preview { get; }
        public Dictionary<string, ResourcePlan> Resources { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string?> BaseFileStates { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, PayloadFilePlan> PayloadFiles { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public bool HasArtChanges { get; set; }
        public bool HasAudioChanges { get; set; }

        public XDocument SourceTemplates { get; private set; } = null!;
        public XDocument TargetTemplates { get; private set; } = null!;
        public XDocument TargetAbilities { get; private set; } = null!;
        public XDocument SourceArtDefinitions { get; private set; } = null!;
        public XDocument TargetArtDefinitions { get; private set; } = null!;
        public XDocument SourceCardAudio { get; private set; } = null!;
        public XDocument TargetCardAudio { get; private set; } = null!;
        public JsonObject SourceBundledAssets { get; private set; } = null!;
        public JsonObject TargetBundledAssets { get; private set; } = null!;
        public JsonObject SourceAudioCatalog { get; private set; } = null!;
        public JsonObject TargetAudioCatalog { get; private set; } = null!;
        public JsonObject SourceEventInclusion { get; private set; } = null!;
        public JsonObject TargetEventInclusion { get; private set; } = null!;
        public JsonObject SourceSoundbankInclusion { get; private set; } = null!;
        public JsonObject TargetSoundbankInclusion { get; private set; } = null!;

        public void LoadDocumentsAndCatalogs()
        {
            SourceTemplates = LoadXml(SourceWorkspace, "Templates.xml");
            TargetTemplates = LoadXml(TargetWorkspace, "Templates.xml");
            TargetAbilities = LoadXml(TargetWorkspace, "Abilities.xml");
            foreach (XElement ability in TargetAbilities.Root?.Elements("Ability") ?? Enumerable.Empty<XElement>())
            {
                int? nId = (int?)ability.Attribute("NId");
                int? id = (int?)ability.Attribute("Id");
                if (nId.HasValue && id.HasValue)
                {
                    _reservedAbilityNIds[nId.Value] = id.Value;
                    _nextAbilityNId = Math.Max(_nextAbilityNId, checked(nId.Value + 1));
                }
            }
            SourceArtDefinitions = LoadXml(SourceWorkspace, "ArtDefinitions.xml");
            TargetArtDefinitions = LoadXml(TargetWorkspace, "ArtDefinitions.xml");
            SourceCardAudio = LoadXml(SourceWorkspace, "CardAudio.xml");
            TargetCardAudio = LoadXml(TargetWorkspace, "CardAudio.xml");

            SourceBundledAssets = LoadCatalog(SourceStreamingAssets, "bundledassets.json");
            TargetBundledAssets = LoadCatalog(TargetStreamingAssets, "bundledassets.json");
            SourceAudioCatalog = LoadCatalog(SourceStreamingAssets, "audio.json");
            TargetAudioCatalog = LoadCatalog(TargetStreamingAssets, "audio.json");
            SourceEventInclusion = LoadCatalog(SourceStreamingAssets, "Audio/event_inclusion.json");
            TargetEventInclusion = LoadCatalog(TargetStreamingAssets, "Audio/event_inclusion.json");
            SourceSoundbankInclusion = LoadCatalog(SourceStreamingAssets, "Audio/soundbank_inclusion.json");
            TargetSoundbankInclusion = LoadCatalog(TargetStreamingAssets, "Audio/soundbank_inclusion.json");

            RegisterBaseFile("data_definitions", TargetDataDefinitionsPath);
            RegisterBaseFile("audio.json", ResolveRequiredFile(TargetStreamingAssets, "audio.json"));
            RegisterBaseFile("bundledassets.json", ResolveRequiredFile(TargetStreamingAssets, "bundledassets.json"));
            RegisterBaseFile(
                "Audio/event_inclusion.json",
                ResolveRequiredFile(TargetStreamingAssets, "Audio/event_inclusion.json"));
            RegisterBaseFile(
                "Audio/soundbank_inclusion.json",
                ResolveRequiredFile(TargetStreamingAssets, "Audio/soundbank_inclusion.json"));
        }

        public int GetNextAbilityNId()
        {
            while (_reservedAbilityNIds.ContainsKey(_nextAbilityNId))
                _nextAbilityNId = checked(_nextAbilityNId + 1);
            return _nextAbilityNId;
        }

        public void ReserveAbilityNId(int nId, int cardId)
        {
            if (_reservedAbilityNIds.TryGetValue(nId, out int ownerId) && ownerId != cardId)
            {
                throw new InvalidDataException(
                    $"NId {nId} уже зарезервирован Ability Id={ownerId}; " +
                    $"не удалось назначить его карте {cardId}.");
            }
            _reservedAbilityNIds[nId] = cardId;
            if (nId >= _nextAbilityNId)
                _nextAbilityNId = checked(nId + 1);
        }

        public void RegisterResource(string relativePath, JsonObject catalogInfo)
        {
            string normalized = relativePath.Replace('\\', '/');
            if (Resources.TryGetValue(normalized, out ResourcePlan? existing))
            {
                if (!JsonNode.DeepEquals(existing.CatalogInfo, catalogInfo))
                    throw new InvalidDataException($"Один ресурс зарегистрирован с разными catalog entries: {normalized}");
                return;
            }
            Resources[normalized] = new ResourcePlan
            {
                RelativePath = normalized,
                CatalogInfo = (JsonObject)catalogInfo.DeepClone()
            };
            Preview.ResourcesToCopy.Add(normalized);
            string targetPath = Path.Combine(
                TargetStreamingAssets,
                normalized.Replace('/', Path.DirectorySeparatorChar));
            BaseFileStates[normalized] = File.Exists(targetPath) ? ComputeSha256(targetPath) : null;
            string targetManifest = targetPath + ".manifest";
            BaseFileStates[normalized + ".manifest"] = File.Exists(targetManifest)
                ? ComputeSha256(targetManifest)
                : null;
        }

        public void RegisterPayloadFile(string relativePath, string physicalPath)
        {
            string normalized = relativePath.Replace('\\', '/');
            PayloadFiles[normalized] = new PayloadFilePlan
            {
                Size = new FileInfo(physicalPath).Length,
                Sha256 = ComputeSha256(physicalPath)
            };
        }

        private void RegisterBaseFile(string relativePath, string physicalPath) =>
            BaseFileStates[relativePath.Replace('\\', '/')] = ComputeSha256(physicalPath);

        private static XDocument LoadXml(string root, string relative) =>
            XDocument.Load(Path.Combine(root, relative), LoadOptions.PreserveWhitespace);

        private static JsonObject LoadCatalog(string root, string relative) =>
            LoadJsonObject(ResolveRequiredFile(root, relative));

        public void Dispose()
        {
            if (Directory.Exists(_temporaryRoot))
                Directory.Delete(_temporaryRoot, recursive: true);
        }
    }
}
