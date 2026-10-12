namespace UI.Models
{
    /// <summary>
    /// Visual style variants for <see cref="UI.Controls.StatusBadge"/>.
    /// </summary>
    public enum StatusBadgeVariant
    {
        /// <summary>Active mining or in-progress drop.</summary>
        Mining,

        /// <summary>Completed and claimed drop.</summary>
        Claimed,

        /// <summary>Marks a chat badge reward (as opposed to an in-game item).</summary>
        Badge
    }
}