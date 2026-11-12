namespace AbilityAnalyzer.Models;

public sealed class ExternalCardImportRecipe
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string SourceGame { get; set; } = "";
    public string SourceStreamingAssets { get; set; } = "";
    public string TargetStreamingAssets { get; set; } = "";
    public string OutputDirectoryName { get; set; } = "ExternalCardImport.output";
    public bool PreserveCatalogTopLevelCrc { get; set; } = true;
    public List<ExternalCardImportEntry> Cards { get; set; } = new();
}

public sealed class ExternalCardImportEntry
{
    public int SourceTemplateId { get; set; }
    public int TargetTemplateId { get; set; }
    public string DebugName { get; set; } = "";
    public int? Availability { get; set; } = 1;
    public int? Provision { get; set; }
    public int? Rarity { get; set; }
    public int? Tier { get; set; }
    public int? Type { get; set; }
    public int? FactionId { get; set; }
    public int? SecondaryFactionId { get; set; }
    public Dictionary<string, string> TemplateOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public int? TargetArtId { get; set; }
    public int? TargetAudioId { get; set; }
    public bool ImportArt { get; set; } = true;
    public string ArtMode { get; set; } = "StandardOnly";
    public bool ImportArtDependencies { get; set; } = true;
    public bool ImportAudio { get; set; } = true;
    public bool IgnoreVoiceovers { get; set; }
    public bool ReplaceExistingTemplate { get; set; }
    public bool ReplaceExistingAbility { get; set; }
    public string AbilityMode { get; set; } = "XmlFile";
    public string AbilityXmlFile { get; set; } = "";
    public int? CloneTargetAbilityFromCardId { get; set; }
    public int? TargetAbilityNId { get; set; }
    public bool ReplaceAbilityIds { get; set; } = true;
    public bool CopySourceName { get; set; } = true;
    public bool CopySourceTooltip { get; set; }
    public bool CopySourceFluff { get; set; } = true;
    public string FallbackLanguage { get; set; } = "en-us";
    public string LocalizationConflictPolicy { get; set; } = "RequireExactMatch";
    public Dictionary<string, ExternalCardLocalizationRecipe> Localization { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ExternalCardLocalizationRecipe
{
    public string Name { get; set; } = "";
    public string Tooltip { get; set; } = "";
    public string Fluff { get; set; } = "";
}

public sealed class ExternalCardImportPreview
{
    public string RecipeName { get; init; } = "";
    public string SourceStreamingAssets { get; init; } = "";
    public string TargetStreamingAssets { get; init; } = "";
    public List<ExternalCardImportCardPreview> Cards { get; } = new();
    public List<string> FilesToModify { get; } = new();
    public List<string> ResourcesToCopy { get; } = new();
    public List<string> Changes { get; } = new();
    public List<string> Warnings { get; } = new();
}

public sealed class ExternalCardImportCardPreview
{
    public int SourceTemplateId { get; init; }
    public int TargetTemplateId { get; init; }
    public string DebugName { get; init; } = "";
    public int SourceArtId { get; init; }
    public int TargetArtId { get; init; }
    public int SourceAudioId { get; init; }
    public int TargetAudioId { get; init; }
    public string AbilityMode { get; init; } = "";
    public List<string> ArtResourcePaths { get; } = new();
    public List<string> AudioEvents { get; } = new();
    public List<string> SoundBanks { get; } = new();
    public List<string> AudioPackages { get; } = new();
}

public sealed class ExternalCardImportResult
{
    public string OutputDirectory { get; init; } = "";
    public string DataDefinitionsFile { get; init; } = "";
    public string ReportFile { get; init; } = "";
    public string ManifestFile { get; init; } = "";
    public string InstallScript { get; init; } = "";
    public string UninstallScript { get; init; } = "";
    public ExternalCardImportPreview Preview { get; init; } = null!;
}
