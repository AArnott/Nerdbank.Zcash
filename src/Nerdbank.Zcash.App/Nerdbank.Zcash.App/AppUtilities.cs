﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Zcash.App;

public static class AppUtilities
{
	internal const ulong SaplingActivationHeight = 419_200;

	public static AccountViewModel? FirstOrDefault(this IEnumerable<AccountViewModel> accountViewModels, Account? accountModel)
	{
		return accountModel is null ? null : accountViewModels.FirstOrDefault(a => a.Account == accountModel);
	}

	public static Uri GetLightServerUrl(this AppSettings settings, ZcashNetwork network) => network switch
	{
		ZcashNetwork.MainNet => settings.LightServerUrl,
		ZcashNetwork.TestNet => settings.LightServerUrlTestNet,
		_ => throw new NotSupportedException(),
	};

	/// <inheritdoc cref="BinarySearch{T}(IReadOnlyList{T}, int, int, T, IComparer{T}?)"/>
	internal static int BinarySearch<T>(this IReadOnlyList<T> sortedList, T item, IComparer<T>? comparer = null) => BinarySearch(sortedList, 0, sortedList.Count, item, comparer);

	/// <summary>
	/// Adds an item to a sorted list.
	/// </summary>
	/// <typeparam name="T">The type of items kept in the list.</typeparam>
	/// <typeparam name="TCollection">The type of collection to update.</typeparam>
	/// <param name="list">The sorted list.</param>
	/// <param name="item">The item to be added.</param>
	/// <param name="comparer">The sorting rules used for <paramref name="list"/>.</param>
	/// <returns>The index of the item in its new position.</returns>
	internal static int AddInSortOrder<T, TCollection>(this TCollection list, T item, IComparer<T> comparer)
		where TCollection : IReadOnlyList<T>, IList<T>
	{
		int index = list.BinarySearch(item, comparer);
		if (index < 0)
		{
			index = ~index;
		}

		list.Insert(index, item);
		return index;
	}

	/// <summary>
	/// Adjusts the position of some item in a sorted list if it does not belong where it currently is.
	/// </summary>
	/// <typeparam name="T">The type of item being sorted.</typeparam>
	/// <typeparam name="TCollection">The type of collection to update.</typeparam>
	/// <param name="list">The list. This must be perfectly sorted according to <paramref name="comparer"/> except possibly for the <paramref name="changedItem"/>.</param>
	/// <param name="changedItem">The item that may not be in the right place.</param>
	/// <param name="comparer">The sorting rules used for <paramref name="list"/>.</param>
	/// <returns>The old and new index for the <paramref name="changedItem"/>.</returns>
	/// <remarks>
	/// When <paramref name="changedItem"/> cannot by found in the <paramref name="list"/>, (-1, -1) is returned.
	/// </remarks>
	internal static (int OldIndex, int NewIndex) UpdateSortPosition<T, TCollection>(this TCollection list, T changedItem, IComparer<T> comparer)
		where TCollection : IReadOnlyList<T>, IList<T>
	{
		IReadOnlyList<T> readOnlyList = list;
		int originalIndex = list.IndexOf(changedItem);
		if (originalIndex < 0)
		{
			return (-1, -1);
		}

		int newIndex = originalIndex;
		if ((originalIndex > 0 && comparer.Compare(changedItem, readOnlyList[originalIndex - 1]) < 0) ||
			(originalIndex < readOnlyList.Count - 2 && comparer.Compare(changedItem, readOnlyList[originalIndex + 1]) > 0) ||
			(originalIndex < readOnlyList.Count - 1 && comparer.Compare(changedItem, readOnlyList[^1]) > 0))
		{
			// The order needs to change.
			list.RemoveAt(originalIndex);
			newIndex = BinarySearch(list, changedItem, comparer);
			if (newIndex < 0)
			{
				newIndex = ~newIndex;
			}

			list.Insert(newIndex, changedItem);
		}

		return (originalIndex, newIndex);
	}

	/// <summary>
	/// Searches a range of elements in the sorted <see cref="IReadOnlyList{T}"/>
	/// for an element using the specified comparer and returns the zero-based index
	/// of the element.
	/// </summary>
	/// <typeparam name="T">The type of element to find.</typeparam>
	/// <param name="sortedList">The list to search.</param>
	/// <param name="start">The zero-based starting index of the range to search.</param>
	/// <param name="count"> The length of the range to search.</param>
	/// <param name="item">The object to locate. The value can be null for reference types.</param>
	/// <param name="comparer">
	/// The <see cref="IComparer{T}"/> implementation to use when comparing
	/// elements, or null to use the default comparer <see cref="Comparer{T}.Default"/>.
	/// </param>
	/// <returns>
	/// The zero-based index of item in the sorted <see cref="IReadOnlyList{T}"/>,
	/// if item is found; otherwise, a negative number that is the bitwise complement
	/// of the index of the next element that is larger than item or, if there is
	/// no larger element, the bitwise complement of <see cref="IReadOnlyCollection{T}.Count"/>.
	/// </returns>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="start"/> is less than 0.-or-<paramref name="count"/> is less than 0.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// <paramref name="start"/> and <paramref name="count"/> do not denote a valid range in the <see cref="IReadOnlyList{T}"/>.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// <paramref name="comparer"/> is null, and the default comparer <see cref="Comparer{T}.Default"/>
	/// cannot find an implementation of the <see cref="IComparable{T}"/> generic interface
	/// or the <see cref="IComparable"/> interface for type <typeparamref name="T"/>.
	/// </exception>
	/// <devremarks>
	/// This implementation heavily inspired by <see href="https://github.com/dotnet/runtime/blob/72d643d05ab23888f30a57d447154e36f979f3d1/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/ArraySortHelper.cs#L77-L95">the
	/// copy in the .NET runtime</see>.
	/// </devremarks>
	private static int BinarySearch<T>(this IReadOnlyList<T> sortedList, int start, int count, T item, IComparer<T>? comparer = null)
	{
		Requires.Range(start >= 0, nameof(start));
		Requires.Range(count >= 0, nameof(count));
		comparer = comparer ?? Comparer<T>.Default;

		int lo = start;
		int hi = start + count - 1;
		while (lo <= hi)
		{
			int i = lo + ((hi - lo) >> 1);
			int order = comparer.Compare(sortedList[i], item);
			switch (order)
			{
				case 0: return i;
				case < 0: lo = i + 1; break;
				case > 0: hi = i - 1; break;
			}
		}

		return ~lo;
	}
}