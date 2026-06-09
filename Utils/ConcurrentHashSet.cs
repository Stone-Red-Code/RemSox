using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

/// <summary>
/// Represents a thread-safe hash set, backed by a <see cref="ConcurrentDictionary{TKey, TValue}"/>.
/// </summary>
/// <typeparam name="T">The type of elements in the set.</typeparam>
public partial class ConcurrentHashSet<T> :
    ICollection<T>,
    IReadOnlyCollection<T>,
    ICollection
    where T : notnull
{
    // The dummy value stored for every key — we only care about keys.
    private static readonly byte DummyValue = 0;

    private readonly ConcurrentDictionary<T, byte> _dictionary;

    // -------------------------------------------------------------------------
    // Constructors
    // -------------------------------------------------------------------------

    /// <summary>Initializes a new, empty instance using the default comparer.</summary>
    public ConcurrentHashSet()
    {
        _dictionary = new ConcurrentDictionary<T, byte>();
    }

    /// <summary>Initializes a new instance that contains elements copied from the specified collection.</summary>
    public ConcurrentHashSet(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _dictionary = new ConcurrentDictionary<T, byte>(
            collection.Select(item => new KeyValuePair<T, byte>(item, DummyValue)));
    }

    /// <summary>Initializes a new instance that contains elements copied from the specified collection
    /// and uses the specified equality comparer.</summary>
    public ConcurrentHashSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _dictionary = new ConcurrentDictionary<T, byte>(
            collection.Select(item => new KeyValuePair<T, byte>(item, DummyValue)),
            comparer);
    }

    /// <summary>Initializes a new, empty instance using the specified equality comparer.</summary>
    public ConcurrentHashSet(IEqualityComparer<T>? comparer)
    {
        _dictionary = new ConcurrentDictionary<T, byte>(comparer);
    }

    /// <summary>Initializes a new instance with the specified concurrency level, initial collection,
    /// and equality comparer.</summary>
    public ConcurrentHashSet(int concurrencyLevel, IEnumerable<T> collection, IEqualityComparer<T>? comparer)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _dictionary = new ConcurrentDictionary<T, byte>(
            concurrencyLevel,
            collection.Select(item => new KeyValuePair<T, byte>(item, DummyValue)),
            comparer);
    }

    /// <summary>Initializes a new, empty instance with the specified concurrency level and initial capacity.</summary>
    public ConcurrentHashSet(int concurrencyLevel, int capacity)
    {
        _dictionary = new ConcurrentDictionary<T, byte>(concurrencyLevel, capacity);
    }

    /// <summary>Initializes a new, empty instance with the specified concurrency level, initial capacity,
    /// and equality comparer.</summary>
    public ConcurrentHashSet(int concurrencyLevel, int capacity, IEqualityComparer<T>? comparer)
    {
        _dictionary = new ConcurrentDictionary<T, byte>(concurrencyLevel, capacity, comparer);
    }

    // -------------------------------------------------------------------------
    // Public properties
    // -------------------------------------------------------------------------

    /// <summary>Gets the number of elements contained in the set.</summary>
    public int Count => _dictionary.Count;

    /// <summary>Gets a value indicating whether the set is empty.</summary>
    public bool IsEmpty => _dictionary.IsEmpty;

    // -------------------------------------------------------------------------
    // Public methods
    // -------------------------------------------------------------------------

    /// <summary>Removes all elements from the set.</summary>
    public void Clear() => _dictionary.Clear();

    /// <summary>Determines whether the set contains the specified element.</summary>
    public bool Contains(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        return _dictionary.ContainsKey(item);
    }

    /// <summary>Returns an enumerator that iterates through the elements of the set.</summary>
    public IEnumerator<T> GetEnumerator() => _dictionary.Keys.GetEnumerator();

    /// <summary>
    /// Returns the element from the set if it already exists, or adds and returns
    /// the specified item if it does not.
    /// </summary>
    /// <param name="item">The element to get or add.</param>
    /// <returns>The existing element if found; otherwise <paramref name="item"/> after it was added.</returns>
    public T GetOrAdd(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));

        // TryAdd is atomic; if it fails the item was already present.
        _dictionary.TryAdd(item, DummyValue);

        // Because ConcurrentDictionary keys are de-duplicated by the comparer,
        // we need to retrieve the canonical key that is actually stored.
        // Keys returns a snapshot; iterate to find the stored instance.
        foreach (T key in _dictionary.Keys)
        {
            if (_dictionary.Comparer.Equals(key, item))
                return key;
        }

        // Fallback — should not happen in practice.
        return item;
    }

    /// <summary>
    /// Adds the specified element to the set. Duplicate elements are silently ignored.
    /// This overload exists to support collection initializer syntax (<c>new ConcurrentHashSet&lt;T&gt; { item }</c>).
    /// </summary>
    public void Add(T item) => TryAdd(item);

    /// <summary>Attempts to add the specified element to the set.</summary>
    /// <returns><see langword="true"/> if the element was added; <see langword="false"/> if it was already present.</returns>
    public bool TryAdd(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        return _dictionary.TryAdd(item, DummyValue);
    }

    /// <summary>Attempts to remove the specified element from the set.</summary>
    /// <returns><see langword="true"/> if the element was removed; <see langword="false"/> if it was not found.</returns>
    public bool TryRemove(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        return _dictionary.TryRemove(item, out _);
    }

    /// <summary>Copies the elements of the set to a new array.</summary>
    public T[] ToArray() => [.. _dictionary.Keys];

    /// <summary>Returns a (non-thread-safe) <see cref="HashSet{T}"/> snapshot of the current elements.</summary>
    public HashSet<T> ToHashSet() => new(_dictionary.Keys, _dictionary.Comparer);

    // -------------------------------------------------------------------------
    // ICollection<T> explicit implementation
    // -------------------------------------------------------------------------

    bool ICollection<T>.IsReadOnly => false;

    void ICollection<T>.Add(T item) => Add(item);

    bool ICollection<T>.Contains(T item) => Contains(item);

    void ICollection<T>.CopyTo(T[] array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        // Take a snapshot to avoid races during copy.
        T[] snapshot = ToArray();
        Array.Copy(snapshot, 0, array, index, snapshot.Length);
    }

    bool ICollection<T>.Remove(T item) => TryRemove(item);

    // -------------------------------------------------------------------------
    // ICollection (non-generic) explicit implementation
    // -------------------------------------------------------------------------

    /// <remarks>
    /// <see cref="ConcurrentDictionary{TKey,TValue}"/> does not expose a meaningful
    /// sync root; following the same convention we return <see langword="false"/> for
    /// <see cref="ICollection.IsSynchronized"/> and <see langword="this"/> for
    /// <see cref="ICollection.SyncRoot"/>, mirroring the BCL approach.
    /// </remarks>
    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (array is T[] typedArray)
        {
            ((ICollection<T>)this).CopyTo(typedArray, index);
            return;
        }

        // Slower path for object arrays (e.g. object[]).
        T[] snapshot = ToArray();
        Array.Copy(snapshot, 0, array, index, snapshot.Length);
    }

    // -------------------------------------------------------------------------
    // IEnumerable explicit implementation
    // -------------------------------------------------------------------------

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}