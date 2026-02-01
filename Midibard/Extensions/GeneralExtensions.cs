using System;

namespace MidiBard.Extensions.General;

public static class GeneralExtensions
{
    public static T Clamp<T>(this T value, T Tmin, T Tmax) where T : IComparable<T>
    {
        if (value.CompareTo(Tmin) < 0) return Tmin;
        if (value.CompareTo(Tmax) > 0) return Tmax;
        return value;
    }

    public static T Cycle<T>(this T value, T Tmin, T Tmax) where T : IComparable<T>
    {
        if (value.CompareTo(Tmin) < 0) return Tmax;
        if (value.CompareTo(Tmax) > 0) return Tmin;
        return value;
    }
}
