namespace DreamDustAutomation.UI
{
    // Logical pixels, before the game's canvas scale. Keep both columns equal
    // in height; reserve concise information, not an entire empty panel.
    internal static class MachineGeometry
    {
        internal const int WorkspaceWidth = 280;
        internal const int SortWidth = 64;
        internal const int SortX = WorkspaceWidth - SortWidth - 2;
        internal const int ControlWidth = 220;
        internal const int PreviewHeight = 76;
        internal const int ResidueHeight = 44;
        internal const int InfoCardWidth = (WorkspaceWidth - 4) / 2;
        internal const int InfoCardHeight = PreviewHeight + ResidueHeight + 4;
        internal const int StorageHeight = 208;
        internal const int ControlHeight = PreviewHeight + ResidueHeight + StorageHeight + 8;
    }
}
