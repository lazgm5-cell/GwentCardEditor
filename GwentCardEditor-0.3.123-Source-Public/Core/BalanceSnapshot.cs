using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace GwentCardEditor;

public sealed record BalanceCard(int Id, int Type, int Power, int Provision);

public sealed record BalanceSnapshot(int Schema, string Source, DateTimeOffset CapturedUtc,
    string? ServerDate, string ContentHash, BalanceCard[] Cards)
{
    public const string OfficialSource = "https://www.playgwent.com/en/decks/builder/api/search?limit=2000&offset=0";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static BalanceSnapshot Create(IEnumerable<BalanceCard> cards, DateTimeOffset captured, string? serverDate = null)
    {
        var sorted = cards.OrderBy(c => c.Id).ToArray();
        var result = new BalanceSnapshot(1, OfficialSource, captured, serverDate, Digest(sorted), sorted);
        result.Validate();
        return result;
    }
    private static string Digest(BalanceCard[] cards) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(cards)));
    public void Validate()
    {
        if (Schema != 1 || Source != OfficialSource || Cards == null || Cards.Length is < 1000 or >= 2000
            || CapturedUtc < new DateTimeOffset(2023, 10, 1, 0, 0, 0, TimeSpan.Zero)
            || CapturedUtc > DateTimeOffset.UtcNow.AddDays(1)
            || Cards.Any(c => c == null || c.Id <= 0 || c.Type <= 0 || c.Power is < 0 or > 9999 || c.Provision is < 0 or > 999)
            || Cards.Select(c => c.Id).Distinct().Count() != Cards.Length
            || !Cards.Select(c => c.Id).SequenceEqual(Cards.Select(c => c.Id).Order())
            || Digest(Cards) != ContentHash) throw new InvalidOperationException("InvalidBalanceSnapshot");
    }
    public void Save(string path)
    {
        Validate();
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, Json));
            File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static BalanceSnapshot Load(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidOperationException("InvalidBalanceSnapshot");
        var result = JsonSerializer.Deserialize<BalanceSnapshot>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("InvalidBalanceSnapshot");
        result.Validate();
        return result;
    }
    public static BalanceSnapshot ParseOfficial(string json, DateTimeOffset captured, string? serverDate = null)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("InvalidBalanceSnapshot");
        var cards = new List<BalanceCard>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var source = row.GetProperty("_source");
            int id = source.GetProperty("id").GetInt32();
            if (row.GetProperty("_id").GetString() != id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new InvalidOperationException("InvalidBalanceSnapshot");
            cards.Add(new(id, source.GetProperty("type").GetInt32(), source.GetProperty("power").GetInt32(), source.GetProperty("provisions_cost").GetInt32()));
        }
        return Create(cards, captured, serverDate);
    }
    public static async Task<BalanceSnapshot> DownloadAsync(CancellationToken cancellation = default)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 16 * 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GwentCardEditor/0.3.114");
        using var response = await client.GetAsync(OfficialSource, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType != "application/json"
            || response.Content.Headers.ContentLength > 16 * 1024 * 1024) throw new InvalidOperationException("InvalidBalanceSnapshot");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[65536];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > 16 * 1024 * 1024) throw new InvalidOperationException("InvalidBalanceSnapshot");
            memory.Write(buffer, 0, read);
        }
        var snapshot = ParseOfficial(System.Text.Encoding.UTF8.GetString(memory.ToArray()), DateTimeOffset.UtcNow, response.Headers.Date?.ToString("O"));
        // Detect a server-side page cap instead of silently accepting a truncated card list.
        using var tail = await client.GetAsync("https://www.playgwent.com/en/decks/builder/api/search?limit=1&offset="
            + snapshot.Cards.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellation).ConfigureAwait(false);
        tail.EnsureSuccessStatusCode();
        using var tailJson = JsonDocument.Parse(await tail.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
        if (tailJson.RootElement.ValueKind != JsonValueKind.Array || tailJson.RootElement.GetArrayLength() != 0)
            throw new InvalidOperationException("InvalidBalanceSnapshot");
        return snapshot;
    }
}

public sealed class BalanceReference
{
    // Accept only the user's own unmodified templates from the verified client release.
    public const string TemplateHash = "69B67958F2B3AE46257061DE6171EA2C423821D1C8DFC760F25605D6C2BCC9EA";
    // Verified Templates.xml from the official GOG data_definitions archive
    // F0856469EF914763B9A7D6B8F46B928E5DAB6B2B1B31BB5C545D9EF6882823AF.
    private static readonly HashSet<string> VerifiedTemplateHashes = new(StringComparer.Ordinal)
    {
        TemplateHash,
        "43D3AE986CC6C08CB951940E45E0EEA85581FDD6A0BB89AA8F9B316B9E96CFBE"
    };
    public IReadOnlyDictionary<int, BalanceCard> Cards { get; }
    private BalanceReference(Dictionary<int, BalanceCard> cards) => Cards = cards;
    public static BalanceReference Load(string directory)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, "Templates.xml"));
        if (!VerifiedTemplateHashes.Contains(Convert.ToHexString(SHA256.HashData(bytes))))
            throw new InvalidOperationException("BalanceReferenceMismatch");
        using var stream = new MemoryStream(bytes);
        var xml = XDocument.Load(stream);
        return new(xml.Root!.Elements("Template").Where(e => (string?)e.Attribute("Type") == "CardTemplate")
            .ToDictionary(e => (int)e.Attribute("Id")!, e => new BalanceCard((int)e.Attribute("Id")!, (int)e.Element("Type")!, (int)e.Element("Power")!, (int)e.Element("Provision")!)));
    }
}

public sealed class BalanceProjection
{
    private readonly IReadOnlyDictionary<int, BalanceCard> baseline;
    private readonly Dictionary<int, BalanceCard> current;
    public BalanceSnapshot Snapshot { get; }
    public BalanceProjection(BalanceReference reference, BalanceSnapshot snapshot)
    {
        snapshot.Validate();
        baseline = reference.Cards;
        current = snapshot.Cards.ToDictionary(c => c.Id);
        if (current.Values.Count(c => baseline.TryGetValue(c.Id, out var b) && b.Type == c.Type && c.Type is 2 or 4 or 8) < 800)
            throw new InvalidOperationException("BalanceCoverageMismatch");
        Snapshot = snapshot;
    }
    public bool TryDelta(CardEntry local, out BalanceCard delta)
    {
        delta = new(local.Id, local.Type, 0, 0);
        if (!baseline.TryGetValue(local.Id, out var original) || !current.TryGetValue(local.Id, out var official)
            || original.Type != local.Type || official.Type != local.Type || local.Type is not (2 or 4 or 8) || local.IsToken) return false;
        delta = new(local.Id, local.Type, local.Type == 4 ? official.Power - original.Power : 0, official.Provision - original.Provision);
        return true;
    }
    public CardEntry Display(CardEntry local) => TryDelta(local, out var delta)
        ? local with { Power = checked(local.Power + delta.Power), Provision = checked(local.Provision + delta.Provision) }
        : local with { Power = -1, Provision = -1 };
    public int ToStored(CardEntry local, string field, int requested)
    {
        if (!TryDelta(local, out var delta)) throw new InvalidOperationException("BalanceCardMissing");
        int offset = field == "Power" ? delta.Power : field == "Provision" ? delta.Provision : throw new ArgumentException(nameof(field));
        int result = checked(requested - offset);
        if (result == (field == "Power" ? local.Power : local.Provision)) return result;
        if (result < (field == "Power" && local.Type == 4 ? 1 : 0) || result > (field == "Power" ? 999 : 99))
            throw new InvalidOperationException("BalanceRange");
        return result;
    }
}
