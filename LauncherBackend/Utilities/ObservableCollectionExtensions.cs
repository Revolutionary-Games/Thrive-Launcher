namespace LauncherBackend.Utilities;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using DynamicData;

public static class ObservableCollectionExtensions
{
    public static void ApplyChangeFromAnotherCollection<T>(this ObservableCollection<T> collection,
        NotifyCollectionChangedEventArgs args)
    {
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Add:
                collection.AddOrInsertRange(args.NewItems!.Cast<T>(),
                    args.NewStartingIndex);

                break;
            case NotifyCollectionChangedAction.Remove:
                for (int i = 0; i < args.OldItems!.Count; ++i)
                {
                    collection.RemoveAt(args.OldStartingIndex);
                }

                break;
            case NotifyCollectionChangedAction.Replace:
                for (int i = 0; i < args.OldItems!.Count; ++i)
                {
                    collection.RemoveAt(args.OldStartingIndex);
                }

                goto case NotifyCollectionChangedAction.Add;

            // For now move is not implemented
            // case NotifyCollectionChangedAction.Move:
            //     break;
            case NotifyCollectionChangedAction.Reset:
                collection.Clear();
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
