using System.Globalization;
using System.Xml.Linq;
using AbilityAnalyzer.Services;

namespace GwentCardEditor;

public sealed partial class EditorProject
{
    private bool BanArdDescriptionContract(int id)
    {
        if (id != 200048 || SharedAbility(id) || !Card(id).Editable || CurrentAbility(id) is not { } graph) return false;
        int[] numbers = graph.Descendants().Where(e => (string?)e.Attribute("Type") == "IntVar").Select(e => (int?)e.Attribute("Id")).OfType<int>().ToArray();
        return EffectFingerprint(graph, numbers) == "7026C93F18CFC2F92864F840A5F22EEE52AE89E887B46976D9A99E944384BE2F"
            && graph.Descendants().Where(e => (string?)e.Attribute("Type") == "IntVar").All(e => int.TryParse((string?)e.Attribute("V"), out _))
            && new[] { 1, 110, 182 }.All(key => BanArdNumber(graph, key) is >= 0 and <= 9999)
            && GraphValidator.Validate(graph).ErrorCount == 0;
    }

    private static int BanArdNumber(XElement graph, int key) => (int)graph.Descendants().Single(e => (int?)e.Attribute("Id") == key).Attribute("V")!;

    public bool HasDescriptionRepair(int id) => BanArdDescriptionContract(id) && CurrentAbility(id) is { } graph
        && BanArdNumber(graph, 110) == BanArdNumber(graph, 182) && BanArdNumber(graph, 1) != BanArdNumber(graph, 110);

    private XElement? SyncBanArdDescription(int id, XElement? prepared)
    {
        if (!BanArdDescriptionContract(id)) return prepared;
        var graph = prepared ?? CurrentAbility(id)!;
        int melee = BanArdNumber(graph, 110), ranged = BanArdNumber(graph, 182);
        if (melee != ranged)
        {
            if (prepared != null && (melee != BanArdNumber(CurrentAbility(id)!, 110) || ranged != BanArdNumber(CurrentAbility(id)!, 182)))
                throw new InvalidOperationException("SharedBoostDescriptionConflict");
            return prepared;
        }
        if (melee == BanArdNumber(graph, 1)) return prepared;
        var copy = new XElement(graph);
        copy.Descendants().Single(e => (int?)e.Attribute("Id") == 1).SetAttributeValue("V", melee.ToString(CultureInfo.InvariantCulture));
        return copy;
    }
}
