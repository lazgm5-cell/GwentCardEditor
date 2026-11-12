using GwentCardEditor;

internal static class CatalogTokenTests
{
    private static readonly int[] Moved=
    [203183,203185,203181,203184,203180,203182,203179,202566,112404,112403,201731,201737,201725,203203,203124,203122,
        200320,203077,203237,202702,202700,202701,203127,203021,202662,203188,203238,202788,202790,202926,202789,203170];
    public static IEnumerable<string> Run(EditorProject project)
    {
        var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);passed.Add(name);}
        Check(Moved.Length==32 && Moved.Distinct().Count()==32,"Exactly 32 requested catalog overrides");
        var original=project.Cards.ToDictionary(c=>c.Id);var expected=Moved.ToHashSet();
        Check(project.Cards.Where(c=>CardCatalog.IsCatalogToken(c) && !c.IsToken).Select(c=>c.Id).ToHashSet().SetEquals(expected),"No extra catalog overrides");
        foreach(int id in Moved)
        {
            var card=project.Card(id);
            Check(CardCatalog.IsCatalogToken(card) && !card.IsToken,"Catalog-only classification "+id);
            foreach(string language in new[]{"ru-ru","en-us","zh-cn"})
            foreach(int faction in EditorProject.Factions.Where(f=>f==card.Faction || f==card.SecondaryFaction))
            {
                Check(CardCatalog.Query(project,faction,language,group:CardGroup.Tokens).Count(c=>c.Id==id)==1,"Visible once in tokens "+id+"/"+language+"/"+faction);
                Check(!CardCatalog.Query(project,faction,language,group:CardGroup.Cards).Any(c=>c.Id==id),"Absent from ordinary cards "+id+"/"+language+"/"+faction);
                Check(CardCatalog.Query(project,faction,language,group:CardGroup.All).Count(c=>c.Id==id)==1,"Retained in all cards "+id+"/"+language+"/"+faction);
            }
            if(card.Editable)Check(project.Preview(id,project.CurrentEdit(id)).Count==0,"No-op editing preserved "+id);
        }
        foreach(int faction in EditorProject.Factions)
        {
            var visible=project.Cards.Where(c=>!CardCatalog.IsService(c) && !c.IsTutorial && !CardCatalog.IsHidden(c) && (c.Faction==faction || c.SecondaryFaction==faction)).ToArray();
            var tokens=CardCatalog.Query(project,faction,"ru-ru",group:CardGroup.Tokens);
            var cards=CardCatalog.Query(project,faction,"ru-ru",group:CardGroup.Cards);
            var leaders=CardCatalog.Query(project,faction,"ru-ru",group:CardGroup.Leaders);
            Check(tokens.Select(c=>c.Id).ToHashSet().SetEquals(visible.Where(c=>c.IsToken || expected.Contains(c.Id)).Select(c=>c.Id)),"Token group exact "+faction);
            Check(cards.Select(c=>c.Id).ToHashSet().SetEquals(visible.Where(c=>!c.IsToken && !expected.Contains(c.Id) && c.Type!=1).Select(c=>c.Id)),"Ordinary group exact "+faction);
            Check(leaders.All(c=>c.Type==1 && !CardCatalog.IsCatalogToken(c)),"Leaders remain separate "+faction);
            var ascending=CardCatalog.Query(project,faction,"ru-ru",group:CardGroup.Tokens,sort:CardSort.Id).Select(c=>c.Id).ToArray();
            var descending=CardCatalog.Query(project,faction,"ru-ru",group:CardGroup.Tokens,sort:CardSort.Id,descending:true).Select(c=>c.Id).ToArray();
            Check(ascending.SequenceEqual(ascending.Order()) && descending.SequenceEqual(ascending.Reverse()),"Token sorting preserved "+faction);
        }
        Check(project.Cards.All(c=>c==original[c.Id]) && !project.HasChanges,"Game flags, availability and card data unchanged");
        return passed;
    }
}
