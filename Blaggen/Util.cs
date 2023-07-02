namespace Blaggen;

public class ColCounter<T>
    where T : notnull
{
    private readonly Dictionary<T, int> data = new();

    public void Add(T key, int count)
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

    public void AddOne(T key)
    {
        Add(key, 1);
    }

    public IEnumerable<(T, int)> MostCommon()
    {
        return data
                .OrderByDescending(x => x.Value)
                .Select(x => (x.Key, x.Value))
            ;
    }

    public int GetTotalCount()
    {
        return data.Select(x => x.Value).Sum();
    }

    public void Update(ColCounter<T> rhs)
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

    public IEnumerable<T> Keys => data.Keys;
    public IEnumerable<KeyValuePair<T, int>> Items => data;
}

public static class ColCounterUtil
{
    public static ColCounter<T> ToColCounter<T>(this IEnumerable<T> items) where T : notnull
    {
        var r = new ColCounter<T>();

        foreach (var item in items)
        {
            r.AddOne(item);
        }

        return r;
    }
}
