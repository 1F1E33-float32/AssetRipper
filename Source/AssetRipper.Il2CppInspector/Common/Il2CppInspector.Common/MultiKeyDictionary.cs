using System.Diagnostics.CodeAnalysis;

// *************************************************
// Created by Aron Weiler
// Feel free to use this code in any way you like,
// just don't blame me when your coworkers think you're awesome.
// Comments?  Email aronweiler@gmail.com
// Revision 1.6
// Revised locking strategy based on the some bugs found with the existing lock objects.
// Possible deadlock and race conditions resolved by swapping out the two lock objects for a ReaderWriterLockSlim.
// Performance takes a very small hit, but correctness is guaranteed.
// *************************************************
namespace Aron.Weiler;

/// <summary>
/// Multi-Key Dictionary Class
/// </summary>
/// <typeparam name = "K">Primary Key Type</typeparam>
/// <typeparam name = "L">Sub Key Type</typeparam>
/// <typeparam name = "V">Value Type</typeparam>
public class MultiKeyDictionary<K, L, V>
	where K : notnull
	where L : notnull
{
	internal readonly Dictionary<K, V> BaseDictionary = [];
	internal readonly Dictionary<L, K> SubDictionary = [];
	internal readonly Dictionary<K, L> PrimaryToSubkeyMapping = [];
	readonly ReaderWriterLockSlim _readerWriterLock = new();
	public V this[L subKey]
	{
		get
		{
			if (TryGetValue(subKey, out V? item))
			{
				return item;
			}

			throw new KeyNotFoundException("sub key not found: " + subKey.ToString());
		}
	}

	public V this[K primaryKey]
	{
		get
		{
			if (TryGetValue(primaryKey, out V? item))
			{
				return item;
			}

			throw new KeyNotFoundException("primary key not found: " + primaryKey.ToString());
		}
	}

	public void Associate(L subKey, K primaryKey)
	{
		_readerWriterLock.EnterUpgradeableReadLock();
		try
		{
			if (!BaseDictionary.ContainsKey(primaryKey))
			{
				throw new KeyNotFoundException(string.Format("The base dictionary does not contain the key '{0}'", primaryKey));
			}

			if (PrimaryToSubkeyMapping.ContainsKey(primaryKey)) // Remove the old mapping first
			{
				_readerWriterLock.EnterWriteLock();
				try
				{
					if (SubDictionary.ContainsKey(PrimaryToSubkeyMapping[primaryKey]))
					{
						SubDictionary.Remove(PrimaryToSubkeyMapping[primaryKey]);
					}

					PrimaryToSubkeyMapping.Remove(primaryKey);
				}
				finally
				{
					_readerWriterLock.ExitWriteLock();
				}
			}

			SubDictionary[subKey] = primaryKey;
			PrimaryToSubkeyMapping[primaryKey] = subKey;
		}
		finally
		{
			_readerWriterLock.ExitUpgradeableReadLock();
		}
	}

	public bool TryGetValue(L subKey, [MaybeNullWhen(false)] out V val)
	{
		val = default;
		_readerWriterLock.EnterReadLock();
		try
		{
			if (SubDictionary.TryGetValue(subKey, out K? primaryKey))
			{
				return BaseDictionary.TryGetValue(primaryKey, out val);
			}
		}
		finally
		{
			_readerWriterLock.ExitReadLock();
		}

		return false;
	}

	public bool TryGetValue(K primaryKey, [MaybeNullWhen(false)] out V val)
	{
		_readerWriterLock.EnterReadLock();
		try
		{
			return BaseDictionary.TryGetValue(primaryKey, out val);
		}
		finally
		{
			_readerWriterLock.ExitReadLock();
		}
	}

	public bool ContainsKey(L subKey) => TryGetValue(subKey, out _);

	public bool ContainsKey(K primaryKey) => TryGetValue(primaryKey, out _);

	public void Remove(K primaryKey)
	{
		_readerWriterLock.EnterWriteLock();
		try
		{
			if (PrimaryToSubkeyMapping.ContainsKey(primaryKey))
			{
				if (SubDictionary.ContainsKey(PrimaryToSubkeyMapping[primaryKey]))
				{
					SubDictionary.Remove(PrimaryToSubkeyMapping[primaryKey]);
				}

				PrimaryToSubkeyMapping.Remove(primaryKey);
			}

			BaseDictionary.Remove(primaryKey);
		}
		finally
		{
			_readerWriterLock.ExitWriteLock();
		}
	}

	public void Remove(L subKey)
	{
		_readerWriterLock.EnterWriteLock();
		try
		{
			BaseDictionary.Remove(SubDictionary[subKey]);
			PrimaryToSubkeyMapping.Remove(SubDictionary[subKey]);
			SubDictionary.Remove(subKey);
		}
		finally
		{
			_readerWriterLock.ExitWriteLock();
		}
	}

	public void Add(K primaryKey, V val)
	{
		_readerWriterLock.EnterWriteLock();
		try
		{
			BaseDictionary.Add(primaryKey, val);
		}
		finally
		{
			_readerWriterLock.ExitWriteLock();
		}
	}

	public void Add(K primaryKey, L subKey, V val)
	{
		Add(primaryKey, val);
		Associate(subKey, primaryKey);
	}

	public V[] CloneValues()
	{
		_readerWriterLock.EnterReadLock();
		try
		{
			V[] values = new V[BaseDictionary.Values.Count];
			BaseDictionary.Values.CopyTo(values, 0);
			return values;
		}
		finally
		{
			_readerWriterLock.ExitReadLock();
		}
	}

	public List<V> Values
	{
		get
		{
			_readerWriterLock.EnterReadLock();
			try
			{
				return [.. BaseDictionary.Values];
			}
			finally
			{
				_readerWriterLock.ExitReadLock();
			}
		}
	}

	public K[] ClonePrimaryKeys()
	{
		_readerWriterLock.EnterReadLock();
		try
		{
			K[] values = new K[BaseDictionary.Keys.Count];
			BaseDictionary.Keys.CopyTo(values, 0);
			return values;
		}
		finally
		{
			_readerWriterLock.ExitReadLock();
		}
	}

	public L[] CloneSubKeys()
	{
		_readerWriterLock.EnterReadLock();
		try
		{
			L[] values = new L[SubDictionary.Keys.Count];
			SubDictionary.Keys.CopyTo(values, 0);
			return values;
		}
		finally
		{
			_readerWriterLock.ExitReadLock();
		}
	}

	public void Clear()
	{
		_readerWriterLock.EnterWriteLock();
		try
		{
			BaseDictionary.Clear();
			SubDictionary.Clear();
			PrimaryToSubkeyMapping.Clear();
		}
		finally
		{
			_readerWriterLock.ExitWriteLock();
		}
	}

	public int Count
	{
		get
		{
			_readerWriterLock.EnterReadLock();
			try
			{
				return BaseDictionary.Count;
			}
			finally
			{
				_readerWriterLock.ExitReadLock();
			}
		}
	}

	public IEnumerator<KeyValuePair<K, V>> GetEnumerator()
	{
		_readerWriterLock.EnterReadLock();
		try
		{
			return BaseDictionary.GetEnumerator();
		}
		finally
		{
			_readerWriterLock.ExitReadLock();
		}
	}
}
