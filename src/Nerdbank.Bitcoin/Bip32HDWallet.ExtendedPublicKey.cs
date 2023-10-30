﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;
using ECPubKey = NBitcoin.Secp256k1.ECPubKey;

namespace Nerdbank.Bitcoin;

public static partial class Bip32HDWallet
{
	/// <summary>
	/// A BIP-39 extended public key.
	/// </summary>
	[DebuggerDisplay($"{{{nameof(DebuggerDisplay)},nq}}")]
	public class ExtendedPublicKey : ExtendedKeyBase
	{
		private readonly PublicKey key;
		private readonly FixedArrays fixedArrays;

		/// <summary>
		/// Initializes a new instance of the <see cref="ExtendedPublicKey"/> class.
		/// </summary>
		/// <param name="key">The public key.</param>
		/// <param name="chainCode"><inheritdoc cref="ExtendedKeyBase(ReadOnlySpan{byte}, ReadOnlySpan{byte}, byte, uint, bool)" path="/param[@name='chainCode']"/></param>
		/// <param name="parentFingerprint"><inheritdoc cref="ExtendedKeyBase(ReadOnlySpan{byte}, ReadOnlySpan{byte}, byte, uint, bool)" path="/param[@name='parentFingerprint']"/></param>
		/// <param name="depth"><inheritdoc cref="ExtendedKeyBase(ReadOnlySpan{byte}, ReadOnlySpan{byte}, byte, uint, bool)" path="/param[@name='depth']"/></param>
		/// <param name="childIndex"><inheritdoc cref="ExtendedKeyBase(ReadOnlySpan{byte}, ReadOnlySpan{byte}, byte, uint, bool)" path="/param[@name='childIndex']"/></param>
		/// <param name="testNet"><inheritdoc cref="ExtendedKeyBase(ReadOnlySpan{byte}, ReadOnlySpan{byte}, byte, uint, bool)" path="/param[@name='testNet']"/></param>
		protected internal ExtendedPublicKey(ECPubKey key, ReadOnlySpan<byte> chainCode, ReadOnlySpan<byte> parentFingerprint, byte depth, uint childIndex, bool testNet)
			: base(chainCode, parentFingerprint, depth, childIndex, testNet)
		{
			Requires.NotNull(key);

			this.key = new(key, testNet);
			this.fixedArrays = CreateFixedArrays(key);
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="ExtendedPublicKey"/> class.
		/// </summary>
		/// <param name="copyFrom">The instance to copy from.</param>
		protected ExtendedPublicKey(ExtendedPublicKey copyFrom)
			: base(copyFrom)
		{
			this.key = copyFrom.key;
			this.fixedArrays = CreateFixedArrays(this.Key.CryptographicKey);
		}

		/// <inheritdoc/>
		public override ReadOnlySpan<byte> Identifier => this.fixedArrays.Identifier;

		/// <summary>
		/// Gets the EC public key.
		/// </summary>
		public ECPubKey CryptographicKey => this.key.CryptographicKey;

		/// <summary>
		/// Gets the key.
		/// </summary>
		public PublicKey Key => this.key;

		/// <summary>
		/// Gets the version header for public keys on mainnet.
		/// </summary>
		internal static ReadOnlySpan<byte> MainNet => new byte[] { 0x04, 0x88, 0xB2, 0x1E };

		/// <summary>
		/// Gets the version header for public keys on testnet.
		/// </summary>
		internal static ReadOnlySpan<byte> TestNet => new byte[] { 0x04, 0x35, 0x87, 0xCF };

		/// <inheritdoc/>
		protected override ReadOnlySpan<byte> Version => this.IsTestNet ? TestNet : MainNet;

		/// <inheritdoc/>
		public override ExtendedPublicKey Derive(uint childIndex)
		{
			if ((childIndex & Bip32KeyPath.HardenedBit) != 0)
			{
				throw new NotSupportedException(Strings.CannotDeriveHardenedChildFromPublicKey);
			}

			Span<byte> hashInput = stackalloc byte[PublicKeyLength + sizeof(uint)];
			this.Key.CryptographicKey.WriteToSpan(true, hashInput, out _);
			BitUtilities.WriteBE(childIndex, hashInput[PublicKeyLength..]);

			Span<byte> hashOutput = stackalloc byte[512 / 8];
			HMACSHA512.HashData(this.ChainCode, hashInput, hashOutput);
			Span<byte> childKeyAdd = hashOutput[..32];
			Span<byte> childChainCode = hashOutput[32..];

			// From the spec:
			// In case parse256(IL) ≥ n or Ki is the point at infinity, the resulting key is invalid,
			// and one should proceed with the next value for i.
			if (!this.key.CryptographicKey.TryAddTweak(childKeyAdd, out NBitcoin.Secp256k1.ECPubKey? pubKey))
			{
				throw new InvalidKeyException(Strings.VeryUnlikelyInvalidChildKey);
			}

			byte childDepth = checked((byte)(this.Depth + 1));

			return new ExtendedPublicKey(pubKey, childChainCode, this.Identifier[..4], childDepth, childIndex, this.IsTestNet)
			{
				DerivationPath = this.DerivationPath?.Append(childIndex),
			};
		}

		/// <inheritdoc/>
		protected override int WriteKeyMaterial(Span<byte> destination)
		{
			this.key.CryptographicKey.WriteToSpan(compressed: true, destination, out int written);
			return written;
		}

		private static FixedArrays CreateFixedArrays(ECPubKey key)
		{
			Span<byte> publicKey = stackalloc byte[33];
			key.WriteToSpan(true, publicKey, out int publicKeyLength);

			Span<byte> sha256Hash = stackalloc byte[256 / 8];
			int firstHashLength = SHA256.HashData(publicKey[..publicKeyLength], sha256Hash);

			RipeMD160Digest digest160 = new();
			Span<byte> identifier = stackalloc byte[digest160.GetDigestSize()];
			digest160.BlockUpdate(sha256Hash[..firstHashLength]);
			int finalHashLength = digest160.DoFinal(identifier);

			return new(identifier[..finalHashLength]);
		}

		private unsafe struct FixedArrays
		{
			internal const int IdentifierLength = 160 / 8;
			private fixed byte identifier[IdentifierLength];

			internal FixedArrays(ReadOnlySpan<byte> identifier)
			{
				Requires.Argument(identifier.Length == IdentifierLength, nameof(identifier), "Unexpected length.");
				identifier.CopyTo(this.IdentifierWritable);
			}

			internal readonly ReadOnlySpan<byte> Identifier => MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(this.identifier[0]), IdentifierLength);

			private Span<byte> IdentifierWritable => MemoryMarshal.CreateSpan(ref this.identifier[0], IdentifierLength);
		}
	}
}