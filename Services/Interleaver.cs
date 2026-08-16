public static class Interleaver
{
    public static IReadOnlyList<Candidate> RoundRobin(
        IEnumerable<IReadOnlyList<Candidate>> lists,
        ISet<string> exclude,
        int take)
    {
        var sources = lists.Where(l => l.Count > 0).ToList();
        var result = new List<Candidate>(Math.Min(take, sources.Sum(l => l.Count)));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new int[sources.Count];

        var exhausted = false;
        while (!exhausted && result.Count < take)
        {
            exhausted = true;

            for (var i = 0; i < sources.Count && result.Count < take; i++)
            {
                var list = sources[i];

                while (cursors[i] < list.Count)
                {
                    var candidate = list[cursors[i]++];
                    exhausted = false;

                    if (exclude.Contains(candidate.ArticleId)) continue;
                    if (!seen.Add(candidate.ArticleId)) continue;

                    result.Add(candidate);
                    break;
                }
            }
        }

        return result;
    }
}
