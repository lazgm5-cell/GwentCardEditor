namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    private const string OfficialGogPath = @"G:\Games\GOG Galaxy\Games\Gwent";

    private void OpenOfficialGog()
    {
        if (Unexported && !Ask(UiText.Get("Discard"))) return;
        if (!Directory.Exists(OfficialGogPath))
        {
            MessageBox.Show(this, OfficialGogPath, UiText.Get("OfficialGogNotFound"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        LoadProject(OfficialGogPath);
    }

    private static string DescribeClientSource(EditorProject current, bool includeHash = false)
    {
        string fullSource = Path.GetFullPath(current.SourcePath);
        string gogRoot = Path.GetFullPath(OfficialGogPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        bool officialGog = fullSource.StartsWith(gogRoot, StringComparison.OrdinalIgnoreCase)
            || fullSource.Equals(gogRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        string profile = UiText.Get(officialGog ? "OfficialGogSource" : "CustomClientSource");
        string hash = current.Client?.ArchiveHash ?? "";
        string shortHash = hash.Length > 16 ? hash[..16] + "…" : hash;
        string result = profile + ": " + current.SourcePath;
        if (shortHash.Length > 0) result += "  |  SHA-256 " + shortHash;
        if (!includeHash) return result;
        string baseline = current.Client?.DefaultArchivePath is string path
            ? Environment.NewLine + UiText.Get("DefaultBaseline") + " " + path : "";
        return result + (hash.Length == 0 ? "" : Environment.NewLine + UiText.Get("StatusReportHash") + ": " + hash) + baseline;
    }

    private string InitialStatusReason(InitialStatusSetting setting, IReadOnlyList<InitialStatusDiagnostic> diagnostics)
    {
        if (setting.Reason == "StatusNeedsUnit") return UiText.Get(setting.Reason);
        var relevant = diagnostics.Where(d =>
            d.Code == "FlagDependency" && d.Detail == setting.Key
            || d.Code is "NodeContract" or "ExternalDependency" or "GraphValidation" or "TemplateTokens" or "AbilityOwnership" or "CardEligibility" or "StatusMask" or "StatusFilter" or "StatusWrite" or "StatusTrigger" or "StatusDependency").ToArray();
        if (relevant.Length == 0) return UiText.Get(setting.Reason);
        return string.Join("; ", relevant.Select(FormatInitialStatusDiagnostic).Distinct(StringComparer.Ordinal));
    }

    private string FormatInitialStatusDiagnostic(InitialStatusDiagnostic diagnostic)
    {
        string detail = diagnostic.Detail;
        return diagnostic.Code switch
        {
            "FlagDependency" => UiText.Get("StatusBlockFlag") + ": " + GameKeyword(detail),
            "NodeContract" => UiText.Get("StatusBlockNode") + ": " + detail,
            "ExternalDependency" => UiText.Get("StatusBlockExternal"),
            "GraphValidation" => UiText.Get("StatusBlockGraph"),
            "TemplateTokens" => UiText.Get("StatusBlockTemplate") + ": " + detail,
            "AbilityOwnership" => UiText.Get("StatusBlockOwnership") + ": " + detail,
            "CardEligibility" => UiText.Get("StatusBlockType") + ": " + UiText.Get(detail),
            "StatusMask" or "StatusFilter" or "StatusWrite" or "StatusTrigger" or "StatusDependency"
                => UiText.Get("StatusBlockOther") + ": " + detail,
            _ => diagnostic.Code + ": " + detail
        };
    }

}
