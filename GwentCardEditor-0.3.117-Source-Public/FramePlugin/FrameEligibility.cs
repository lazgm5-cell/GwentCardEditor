namespace GwentCardEditor.FieryFiend;

internal static class FrameEligibility
{
    internal static bool Accept(bool goldTier, bool leader) => goldTier && !leader;
    internal static bool ShouldRender(bool active, bool fullyCulled, bool rendererEnabled, bool globallyPaused) => active && !fullyCulled && rendererEnabled && !globallyPaused;
}
