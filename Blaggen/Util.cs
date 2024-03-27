namespace Blaggen;

internal class ColCounter<T>
    where T : notnull
{
    private readonly Dictionary<T, int> data = new();

    internal void Add(T key, int count)
    {
        if (data.TryGetValue(key, out var value) == false)
        {
            data.Add(key, count);
            return;
        }
        Set(key, value + count);
    }

    private void Set(T key, int value)
    {
        data[key] = value;
    }

    internal void AddOne(T key)
    {
        Add(key, 1);
    }

    internal IEnumerable<(T, int)> MostCommon()
    {
        return data
                .OrderByDescending(x => x.Value)
                .Select(x => (x.Key, x.Value))
            ;
    }

    internal int GetTotalCount()
    {
        return data.Select(x => x.Value).Sum();
    }

    internal void Update(ColCounter<T> rhs)
    {
        foreach (var (key, count) in rhs.data)
        {
            Add(key, count);
        }
    }

    internal void Max(ColCounter<T> rhs)
    {
        foreach (var (key, rhsValue) in rhs.data)
        {
            if (data.TryGetValue(key, out var selfValue))
            {
                Set(key, Math.Max(selfValue, rhsValue));
            }
            else
            {
                Set(key, rhsValue);
            }
        }
    }

    internal IEnumerable<T> Keys => data.Keys;
    internal IEnumerable<KeyValuePair<T, int>> Items => data;
}

internal static class ColCounterUtil
{
    internal static ColCounter<T> ToColCounter<T>(this IEnumerable<T> items) where T : notnull
    {
        var r = new ColCounter<T>();

        foreach (var item in items)
        {
            r.AddOne(item);
        }

        return r;
    }
}
