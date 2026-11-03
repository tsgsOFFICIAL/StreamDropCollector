namespace Core.Helpers
{
    /// <summary>
    /// Pure edits of a game filter (a slug list plus an exclude-mode flag) for the inventory "ignore" shortcuts.
    /// </summary>
    /// <remarks>
    /// An empty list allows every game. In allow mode only listed games are mined; in exclude (blacklist) mode every game
    /// except the listed ones is mined. Each edit returns the new list and mode, never an accidental "allow everything".
    /// </remarks>
    public static class GameFilterEditor
    {
        /// <summary>
        /// Stops a game from being mined: adds it to the exclude list, or removes it from the allow list.
        /// </summary>
        public static (List<string> Slugs, bool Blacklist) Ignore(IReadOnlyList<string> slugs, bool blacklist, string slug)
        {
            slug = Normalize(slug);
            List<string> list = [.. slugs];

            // Nothing is filtered yet: adding to an allow list would drop every other game, so exclude this one instead.
            if (list.Count == 0)
                return ([slug], true);

            if (blacklist)
            {
                if (!Contains(list, slug))
                    list.Add(slug);

                return (list, true);
            }

            list.RemoveAll(x => string.Equals(x, slug, StringComparison.OrdinalIgnoreCase));

            // Removing the last allowed game would empty the list, which allows everything; exclude it instead.
            return list.Count == 0 ? ([slug], true) : (list, false);
        }

        /// <summary>
        /// Lets an ignored game be mined again: removes it from the exclude list, or adds it to the allow list.
        /// </summary>
        public static (List<string> Slugs, bool Blacklist) Unignore(IReadOnlyList<string> slugs, bool blacklist, string slug)
        {
            slug = Normalize(slug);
            List<string> list = [.. slugs];

            if (blacklist)
            {
                list.RemoveAll(x => string.Equals(x, slug, StringComparison.OrdinalIgnoreCase));
                return (list, true);
            }

            // An empty allow list already allows everything.
            if (list.Count > 0 && !Contains(list, slug))
                list.Add(slug);

            return (list, false);
        }

        /// <summary>
        /// Moves a game to the front of the allow list so it is mined first; other filters are left unchanged.
        /// </summary>
        public static (List<string> Slugs, bool Blacklist) MoveToTop(IReadOnlyList<string> slugs, bool blacklist, string slug)
        {
            slug = Normalize(slug);
            List<string> list = [.. slugs];

            if (!CanMoveToTop(slugs, blacklist, slug))
                return (list, blacklist);

            list.RemoveAll(x => string.Equals(x, slug, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, slug);
            return (list, false);
        }

        /// <summary>
        /// Returns whether a game can move up the priority order: only an allow list has an order, and the game must not be first.
        /// </summary>
        public static bool CanMoveToTop(IReadOnlyList<string> slugs, bool blacklist, string slug)
        {
            if (blacklist || slugs.Count < 2)
                return false;

            int index = slugs.ToList().FindIndex(x => string.Equals(x, Normalize(slug), StringComparison.OrdinalIgnoreCase));
            return index > 0;
        }

        private static string Normalize(string slug) => slug.Trim().ToLowerInvariant();

        private static bool Contains(List<string> list, string slug) =>
            list.Any(x => string.Equals(x, slug, StringComparison.OrdinalIgnoreCase));
    }
}
