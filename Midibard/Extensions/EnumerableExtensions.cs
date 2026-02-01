using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MidiBard.Extensions.Enumerable;

static class EnumerableExtensions
{
    // <summary> Iterate over enumerables with additional index. </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static IEnumerable<(T Value, int Index)> WithIndex<T>(this IEnumerable<T> list)
            => list.Select((x, i) => (x, i));

    public static bool TryGetValue<T>(this List<T> list, int index, out T value)
    {
        if (index >= 0 && index < list.Count)
        {
            value = list[index];
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }

    public static TSource MaxElement<TSource, R>(this IEnumerable<TSource> container, Func<TSource, R> valuingFoo) where R : IComparable
    {
        var enumerator = container.GetEnumerator();
        if (!enumerator.MoveNext())
            throw new ArgumentException("Container is empty!");

        var maxElem = enumerator.Current;
        var maxVal = valuingFoo(maxElem);

        while (enumerator.MoveNext())
        {
            var currVal = valuingFoo(enumerator.Current);

            if (currVal.CompareTo(maxVal) > 0)
            {
                maxVal = currVal;
                maxElem = enumerator.Current;
            }
        }

        return maxElem;
    }

    public static TSource MinElement<TSource, R>(this IEnumerable<TSource> container, Func<TSource, R> valuingFoo) where R : IComparable
    {
        var enumerator = container.GetEnumerator();
        if (!enumerator.MoveNext())
            throw new ArgumentException("Container is empty!");

        var maxElem = enumerator.Current;
        var maxVal = valuingFoo(maxElem);

        while (enumerator.MoveNext())
        {
            var currVal = valuingFoo(enumerator.Current);

            if (currVal.CompareTo(maxVal) < 0)
            {
                maxVal = currVal;
                maxElem = enumerator.Current;
            }
        }

        return maxElem;
    }
}
