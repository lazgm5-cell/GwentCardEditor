using GwentCardEditor.FieryFiend;
using System.Text.Json;

int count = 0;
foreach (bool gold in new[] { false, true })
foreach (bool leader in new[] { false, true })
{
    if (FrameEligibility.Accept(gold, leader) != (gold && !leader)) throw new Exception("Gold/leader scope mismatch");
    count++;
}
foreach (bool active in new[] { false, true })
foreach (bool culled in new[] { false, true })
foreach (bool renderer in new[] { false, true })
foreach (bool paused in new[] { false, true })
{
    if (FrameEligibility.ShouldRender(active, culled, renderer, paused) != (active && !culled && renderer && !paused)) throw new Exception("Render visibility/pause mismatch");
    count++;
}
Console.WriteLine(JsonSerializer.Serialize(new { Passed=true, Checks=count, GamePlaybackTested=false }));
