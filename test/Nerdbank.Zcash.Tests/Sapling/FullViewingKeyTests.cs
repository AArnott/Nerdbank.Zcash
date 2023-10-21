﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Nerdbank.Zcash.Sapling;

namespace Sapling;

public class FullViewingKeyTests : TestBase
{
	private readonly ITestOutputHelper logger;
	private readonly FullViewingKey fvk = new Zip32HDWallet(Mnemonic, ZcashNetwork.MainNet).CreateSaplingAccount().FullViewingKey;

	public FullViewingKeyTests(ITestOutputHelper logger)
	{
		this.logger = logger;
	}

	[Theory, PairwiseData]
	public void TextEncoding_TryDecode(bool testNet)
	{
		ZcashNetwork network = testNet ? ZcashNetwork.TestNet : ZcashNetwork.MainNet;
		string expected = testNet
			? "zviewtestsapling15cr64vjtd0x7xh6ytmun4ulp7k93th7xunhrkqrf55q82m892fr6n708hdlq20gj2ydxz2n5ps052lmz20w2ykxfr9dzwu8fnmktv6fxz3eadpdsa53xl4jwk5m2axj87ksfwngjndj5x8fyr2v7a6ykny7t049p"
			: "zviews1lxdtxcc28jx4anvr49m8qz6rdvv6zuff49vc7vj3gmxzkq0vhlkmcvdmv6a0sm2x9rfdf26xcr34xuhyk9sxfct86ylqwwrf6w6z739z7kkj5440anpa5hz9ek33mque0sgxqymatn0yxvva9alajwsx9yd8x6ty";
		Zip32HDWallet wallet = new(Mnemonic, network);
		Zip32HDWallet.Sapling.ExtendedSpendingKey account = wallet.CreateSaplingAccount(0);
		string actual = account.FullViewingKey.WithoutDiversifier.TextEncoding;
		this.logger.WriteLine(actual);
		Assert.Equal(expected, actual);

		Assert.True(FullViewingKey.TryDecode(actual, out _, out _, out FullViewingKey? decoded));
		Assert.Equal(account.FullViewingKey.WithoutDiversifier, decoded);
	}

	[Fact]
	public void TryDecode()
	{
		Assert.True(FullViewingKey.TryDecode(this.fvk.TextEncoding, out DecodeError? decodeError, out string? errorMessage, out FullViewingKey? imported));
		Assert.Null(decodeError);
		Assert.Null(errorMessage);
		Assert.NotNull(imported);
		Assert.Equal(this.fvk.TextEncoding, imported.TextEncoding);
	}

	[Fact]
	public void TryDecode_ViaInterface()
	{
		Assert.True(TryDecodeViaInterface<FullViewingKey>(this.fvk.TextEncoding, out DecodeError? decodeError, out string? errorMessage, out IKeyWithTextEncoding? imported));
		Assert.Null(decodeError);
		Assert.Null(errorMessage);
		Assert.NotNull(imported);
		Assert.Equal(this.fvk.TextEncoding, imported.TextEncoding);
	}

	[Fact]
	public void TryDecode_Fail()
	{
		Assert.False(FullViewingKey.TryDecode("fail", out DecodeError? decodeError, out string? errorMessage, out FullViewingKey? imported));
		Assert.NotNull(decodeError);
		Assert.NotNull(errorMessage);
		Assert.Null(imported);
	}

	[Fact]
	public void TryDecode_ViaInterface_Fail()
	{
		Assert.False(TryDecodeViaInterface<FullViewingKey>("fail", out DecodeError? decodeError, out string? errorMessage, out IKeyWithTextEncoding? imported));
		Assert.NotNull(decodeError);
		Assert.NotNull(errorMessage);
		Assert.Null(imported);
	}
}
