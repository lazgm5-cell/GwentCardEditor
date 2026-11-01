namespace GwentCardEditor;

internal sealed partial class EditorForm
{
    public void SmokeCatalogTokens(string output)
    {
        Directory.CreateDirectory(output);
        int[] ids=[203183,203185,203181,203184,203180,203182,203179,202566,112404,112403,201731,201737,201725,203203,203124,203122,
            200320,203077,203237,202702,202700,202701,203127,203021,202662,203188,203238,202788,202790,202926,202789,203170];
        foreach(int locale in Enumerable.Range(0,3))
        {
            language.SelectedIndex=locale;
            foreach(int id in ids)
            {
                NavigateToCard(id);Application.DoEvents();
                if(selectedId!=id || currentGroup!=(int)CardGroup.Tokens || group.SelectedIndex!=(int)CardGroup.Tokens
                    || project!.Card(id).IsToken || currentHidden)throw new Exception("Catalog token navigation failed "+id);
                if(project.Card(id).Editable && project.Preview(id,Draft()).Count!=0)throw new Exception("Navigation changed card settings "+id);
            }
            using(var capture=new Bitmap(Width,Height))
            {DrawToBitmap(capture,new Rectangle(Point.Empty,Size));capture.Save(Path.Combine(output,"catalog-tokens-"+UiText.Language+".png"));}
            NavigateToCard(112103);Application.DoEvents();
            if(currentGroup!=(int)CardGroup.Cards)throw new Exception("Ordinary card navigation changed");
            NavigateToCard(132304);Application.DoEvents();
            if(currentGroup!=(int)CardGroup.Tokens || !project!.Card(132304).IsToken)throw new Exception("Native token navigation changed");
        }
        if(project!.HasChanges || draftDirty)throw new Exception("Catalog smoke modified project");
        File.WriteAllText(Path.Combine(output,"ui-smoke.json"),"{\"Passed\":true,\"Languages\":3,\"MovedCards\":32,\"NativeAndOrdinaryPreserved\":true,\"GameFlagsUnchanged\":true,\"ClientModified\":false}");
    }
}
