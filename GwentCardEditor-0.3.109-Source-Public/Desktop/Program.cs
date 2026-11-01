namespace GwentCardEditor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception e)
        {
            int index = Array.IndexOf(args, "--smoke-test");
            string output = index >= 0 && index + 1 < args.Length ? args[index + 1] : EditorSettings.Home;
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "failure.txt"), e.ToString());
            Environment.ExitCode = 1;
            if (index < 0 && !args.Contains("--cache-balance") && !args.Contains("--translation-template") && !args.Contains("--meaning-audit"))
                MessageBox.Show(e.Message, "GWENT Card Editor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private static void Run(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string? Value(string key) => Array.IndexOf(args, key) is int i && i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        if (Value("--cache-balance") is string balanceFile)
        {
            var snapshot = BalanceSnapshot.Load(balanceFile);
            var savedSettings = EditorSettings.Load();
            savedSettings.BalanceSnapshotFile = EditorSettings.CacheSnapshot(snapshot);
            savedSettings.Save();
            return;
        }
        if (Value("--meaning-audit") is string audit)
        {
            string source = Value("--client") ?? Value("--project") ?? throw new ArgumentException("--client or --project required");
            string cache = Path.Combine(Path.GetTempPath(), "GwentMeaningAudit-" + Guid.NewGuid().ToString("N"));
            try { ClientSource.Open(source, cache).ExportAbilityMeaningAudit(audit); }
            finally { if (Directory.Exists(cache)) Directory.Delete(cache, true); }
            return;
        }
        if (Value("--translation-template") is string template)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(template))!);
            File.WriteAllText(template, System.Text.Json.JsonSerializer.Serialize(UiText.EnglishTemplate, new System.Text.Json.JsonSerializerOptions
                { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            return;
        }
        string? smoke = Value("--smoke-test");
        EditorSettings settings = smoke == null ? EditorSettings.Load() : new EditorSettings();
        string? project = Value("--client") ?? Value("--project") ?? settings.LastSource;
        using var window = new EditorForm(project, settings, smoke != null);
        if (smoke != null)
        {
            window.Shown += (_, _) => window.BeginInvoke(async () =>
            {
                try
                {
                    if (args.Contains("--export-continuation-ui-only")) window.SmokeExportContinuation(smoke);
                    else if (args.Contains("--selected-draw-ui-only")) window.SmokeSelectedDrawText(smoke);
                    else if (args.Contains("--discard-text-ui-only")) window.SmokeDiscardText(smoke);
                    else if (args.Contains("--move-text-ui-only")) window.SmokeMoveText(smoke);
                    else if (args.Contains("--draw-text-ui-only")) window.SmokeDrawText(smoke);
                    else if (args.Contains("--target-status-ui-only")) window.SmokeTargetStatusText(smoke);
                    else if (args.Contains("--ban-ard-ui-only")) window.SmokeBanArdDescription(smoke);
                    else if (args.Contains("--target-power-ui-only")) window.SmokeTargetPowerText(smoke);
                    else if (args.Contains("--target-count-ui-only")) window.SmokeTargetCountText(smoke);
                    else if (args.Contains("--cardback-ui-only")) await window.SmokeCardbacks(smoke);
                    else if (args.Contains("--insanity-ui-only")) window.SmokeInsanity(smoke);
                    else if (args.Contains("--catalog-tokens-ui-only")) window.SmokeCatalogTokens(smoke);
                    else if (args.Contains("--flanking-ui-only")) window.SmokeFlanking(smoke);
                    else if (args.Contains("--echo-ui-only")) window.SmokeEcho(smoke);
                    else if (args.Contains("--resupply-ui-only")) window.SmokeResupply(smoke);
                    else if (args.Contains("--syndicate-ui-only")) window.SmokeSyndicate(smoke);
                    else if (args.Contains("--review-batch-ui-only")) window.SmokeReviewBatch(smoke);
                    else if (args.Contains("--leader-copy-theme-ui-only")) window.SmokeLeaderCopyTheme(smoke);
                    else if (args.Contains("--card-clarity-revision-ui-only")) window.SmokeCardClarityRevision(smoke);
                    else if (args.Contains("--additional-ui-only")) window.SmokeAdditionalAbilities(smoke);
                    else if (args.Contains("--description-ui-only")) window.SmokeDescriptionBindings(smoke);
                    else if (args.Contains("--clarity-ui-only")) window.SmokeAbilityClarity(smoke);
                    else if (args.Contains("--catalog-ui-only")) window.SmokeCatalogVisibility(smoke);
                    else if (args.Contains("--number-labels-only")) window.SmokeNumberLabels(smoke);
                    else if (args.Contains("--media-only")) await window.SmokeMediaOnly(smoke);
                    else if (args.Contains("--mp3-only")) await window.SmokeMp3Only(smoke);
                    else if (args.Contains("--custom-audio-only")) await window.SmokeCustomAudioOnly(smoke);
                    else if (args.Contains("--audio-only")) await window.SmokeAudioOnly(smoke);
                    else if (args.Contains("--visuals-only")) window.SmokeVisualsOnly(smoke);
                    else if (args.Contains("--art-only")) await window.SmokeArtOnly(smoke);
                    else if (args.Contains("--related-only")) window.SmokeRelatedOnly(smoke);
                    else if (args.Contains("--statuses-only")) window.SmokeStatusesOnly(smoke);
                    else window.SmokeTest(smoke, Value("--balance-snapshot"));
                }
                catch (Exception e)
                {
                    Directory.CreateDirectory(smoke);
                    File.WriteAllText(Path.Combine(smoke, "failure.txt"), e.ToString());
                    Environment.ExitCode = 1;
                }
                finally { window.CloseForTest(); }
            });
        }
        Application.Run(window);
    }
}
