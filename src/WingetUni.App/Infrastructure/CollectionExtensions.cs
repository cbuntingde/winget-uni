using System.Collections.ObjectModel;

namespace WingetUni.App.Infrastructure;

/// <summary>Small helpers for keeping observable collections in step with a fresh query.</summary>
public static class CollectionExtensions
{
    /// <summary>Replaces a collection's contents with <paramref name="items"/> in one pass.</summary>
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(items);

        collection.Clear();

        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}
