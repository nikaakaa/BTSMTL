using System;

namespace ThirdPersonSimulation
{
    public static class SimulationArraySort
    {
        public static void SortPrefix<T>(T[] values, int count, Comparison<T> comparison)
        {
            for (int root = count / 2 - 1; root >= 0; root--)
                SiftDown(values, root, count, comparison);
            for (int end = count - 1; end > 0; end--)
            {
                T value = values[end];
                values[end] = values[0];
                values[0] = value;
                SiftDown(values, 0, end, comparison);
            }
        }

        static void SiftDown<T>(T[] values, int root, int count, Comparison<T> comparison)
        {
            T value = values[root];
            for (int child = 2 * root + 1; child < count; child = 2 * root + 1)
            {
                if (child + 1 < count && comparison(values[child], values[child + 1]) < 0)
                    child++;
                if (comparison(value, values[child]) >= 0)
                    break;
                values[root] = values[child];
                root = child;
            }
            values[root] = value;
        }
    }
}
