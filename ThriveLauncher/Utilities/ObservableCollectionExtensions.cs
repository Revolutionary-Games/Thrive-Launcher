namespace ThriveLauncher.Utilities;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

public static class ObservableCollectionExtensions
{
    /// <summary>
    ///   Takes a copy of an observable collection in a locked way to guard against exception if the collection is
    ///   attempted to be modified while taking a copy.
    /// </summary>
    /// <param name="collection">The collection to snapshot</param>
    /// <typeparam name="T">Type of collection items</typeparam>
    /// <returns>Safe to use copy of the collection data</returns>
    /// <remarks>
    ///   <para>
    ///     This was added to guard against: https://github.com/Revolutionary-Games/Thrive-Launcher/issues/319
    ///   </para>
    /// </remarks>
    public static List<T> TakeSafeCopy<T>(this ObservableCollection<T> collection)
    {
        lock (collection)
        {
            return collection.ToList();
        }
    }
}
