using System;
using System.Collections.Generic;

namespace ThirdPersonSimulation
{
    public static class SimulationArraySort
    {
        public static void SortPrefix<T>(T[] values, int count, Comparison<T> comparison) =>
            Sort(values.AsSpan(0, count), comparison);

        public static void Sort<T>(Span<T> values, Comparison<T> comparison)
        {
            int count = values.Length;
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

        static void SiftDown<T>(Span<T> values, int root, int count, Comparison<T> comparison)
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

        // 与委托版本相同的堆排序；结构体比较器可被 Burst 编译。
        public static void Sort<T, TComparer>(Span<T> values, TComparer comparer)
            where TComparer : struct, IComparer<T>
        {
            int count = values.Length;
            for (int root = count / 2 - 1; root >= 0; root--)
                SiftDown(values, root, count, ref comparer);
            for (int end = count - 1; end > 0; end--)
            {
                T value = values[end];
                values[end] = values[0];
                values[0] = value;
                SiftDown(values, 0, end, ref comparer);
            }
        }

        static void SiftDown<T, TComparer>(Span<T> values, int root, int count, ref TComparer comparer)
            where TComparer : struct, IComparer<T>
        {
            T value = values[root];
            for (int child = 2 * root + 1; child < count; child = 2 * root + 1)
            {
                if (child + 1 < count && comparer.Compare(values[child], values[child + 1]) < 0)
                    child++;
                if (comparer.Compare(value, values[child]) >= 0)
                    break;
                values[root] = values[child];
                root = child;
            }
            values[root] = value;
        }
    }
}
