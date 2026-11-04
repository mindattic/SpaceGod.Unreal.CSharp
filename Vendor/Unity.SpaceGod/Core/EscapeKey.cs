namespace Unity.SpaceGod.Core
{
    /// <summary>What one press of Esc does in the game (outside the pause menu, which handles its own).</summary>
    public enum EscapeAction
    {
        /// <summary>Close the panels the player clicks through (market, cargo hold, outfitting, saves...).</summary>
        ClosePanels,

        /// <summary>Let the camera go of what it is following, back to free flight.</summary>
        LeaveFocus,

        /// <summary>Clear the selection.</summary>
        Deselect,

        /// <summary>Open the pause menu.</summary>
        OpenPauseMenu,
    }

    /// <summary>
    /// Esc backs out of one layer at a time (U.SPC-T276): an open panel first, then the camera's focus, then the
    /// selection, and with nothing left to back out of it opens the pause menu. At the helm the chase camera and
    /// the selection belong to the flight, so Esc goes straight from the panels to the menu (P lets go of the helm).
    /// </summary>
    public static class EscapeKey
    {
        public static EscapeAction Route(bool panelOpen, bool piloting, bool focused, bool selected)
        {
            if (panelOpen) return EscapeAction.ClosePanels;
            if (piloting) return EscapeAction.OpenPauseMenu;
            if (focused) return EscapeAction.LeaveFocus;
            if (selected) return EscapeAction.Deselect;
            return EscapeAction.OpenPauseMenu;
        }
    }
}
