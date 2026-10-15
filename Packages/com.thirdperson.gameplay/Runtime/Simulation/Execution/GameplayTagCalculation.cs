using System;

namespace ThirdPersonSimulation
{
    public interface IGameplayTagQueryRead
    {
        bool ContainsTag(int tag);
    }

    public static class GameplayTagCalculation
    {
        public static bool IsTagOrParent(int ownedTag, int queryTag, ReadOnlySpan<int> parents)
        {
            int current = ownedTag;
            while (current != 0)
            {
                if (current == queryTag)
                    return true;
                current = parents[current];
            }
            return false;
        }

        public static bool Matches<TRead>(ReadOnlySpan<int> all, ReadOnlySpan<int> any,
            ReadOnlySpan<int> none, in TRead read) where TRead : struct, IGameplayTagQueryRead
        {
            for (int index = 0; index < all.Length; index++)
                if (!read.ContainsTag(all[index]))
                    return false;
            if (any.Length > 0)
            {
                bool matched = false;
                for (int index = 0; index < any.Length; index++)
                {
                    if (!read.ContainsTag(any[index]))
                        continue;
                    matched = true;
                    break;
                }
                if (!matched)
                    return false;
            }
            for (int index = 0; index < none.Length; index++)
                if (read.ContainsTag(none[index]))
                    return false;
            return true;
        }
    }
}
