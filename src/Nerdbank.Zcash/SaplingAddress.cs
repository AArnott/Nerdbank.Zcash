﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Nerdbank.Zcash;

/// <summary>
/// A shielded Zcash address belonging to the <see cref="Pool.Sapling"/> pool.
/// </summary>
public class SaplingAddress : ZcashAddress
{
    private const string MainNetHumanReadablePart = "zs";
    private const string TestNetHumanReadablePart = "ztestsapling";
    private readonly SaplingReceiver receiver;
    private readonly ZcashNetwork network;

    /// <inheritdoc cref="SaplingAddress(ReadOnlySpan{char}, SaplingReceiver, ZcashNetwork)"/>
    public SaplingAddress(SaplingReceiver receiver, ZcashNetwork network = ZcashNetwork.MainNet)
        : base(CreateAddress(receiver, network))
    {
        this.receiver = receiver;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SaplingAddress"/> class.
    /// </summary>
    /// <param name="address"><inheritdoc cref="ZcashAddress(ReadOnlySpan{char})" path="/param"/></param>
    /// <param name="receiver">The encoded receiver.</param>
    /// <param name="network">The network to which this address belongs.</param>
    private SaplingAddress(ReadOnlySpan<char> address, SaplingReceiver receiver, ZcashNetwork network = ZcashNetwork.MainNet)
        : base(address)
    {
        this.network = network;
        this.receiver = receiver;
    }

    /// <inheritdoc/>
    public override ZcashNetwork Network => this.network;

    /// <summary>
    /// Gets the length of the buffers required to decode the address.
    /// </summary>
    /// <returns>The length of the human readable part and data buffers required.</returns>
    /// <exception cref="InvalidAddressException">Thrown if the address is invalid.</exception>
    internal (int HumanReadablePart, int Data) DecodedLength => Bech32.GetDecodedLength(this.Address) ?? throw new InvalidAddressException();

    /// <inheritdoc/>
    internal override byte UnifiedAddressTypeCode => 0x02;

    /// <inheritdoc/>
    internal override int ReceiverEncodingLength => this.receiver.GetReadOnlySpan().Length;

    /// <inheritdoc/>
    public override bool SupportsPool(Pool pool) => pool == Pool.Sapling;

    /// <inheritdoc/>
    public override TPoolReceiver? GetPoolReceiver<TPoolReceiver>() => AsReceiver<SaplingReceiver, TPoolReceiver>(this.receiver);

    /// <inheritdoc cref="ZcashAddress.TryParse(ReadOnlySpan{char}, out ZcashAddress?, out ParseError?, out string?)" />
    internal static bool TryParse(ReadOnlySpan<char> address, [NotNullWhen(true)] out SaplingAddress? result, [NotNullWhen(false)] out ParseError? errorCode, [NotNullWhen(false)] out string? errorMessage)
    {
        ZcashNetwork? network =
            address.StartsWith(MainNetHumanReadablePart, StringComparison.Ordinal) ? ZcashNetwork.MainNet :
            address.StartsWith(TestNetHumanReadablePart, StringComparison.Ordinal) ? ZcashNetwork.TestNet :
            null;
        if (network is null)
        {
            result = null;
            errorCode = ParseError.UnrecognizedAddressType;
            errorMessage = Strings.InvalidSaplingPreamble;
            return false;
        }

        if (Bech32.GetDecodedLength(address) is (int tagLength, int dataLength))
        {
            Span<char> tag = stackalloc char[tagLength];
            Span<byte> data = stackalloc byte[dataLength];
            if (!Bech32.Original.TryDecode(address, tag, data, out DecodeError? decodeError, out errorMessage, out _))
            {
                result = null;
                errorCode = DecodeToParseError(decodeError);
                return false;
            }

            result = new SaplingAddress(address, new SaplingReceiver(data), network.Value);
            errorCode = null;
            errorMessage = null;
            return true;
        }

        result = null;
        errorCode = ParseError.UnrecognizedAddressType;
        errorMessage = string.Format(CultureInfo.CurrentCulture, Strings.InvalidXAddress, "sapling");
        return false;
    }

    /// <inheritdoc/>
    internal override int GetReceiverEncoding(Span<byte> output)
    {
        ReadOnlySpan<byte> receiverSpan = this.receiver.GetReadOnlySpan();
        receiverSpan.CopyTo(output);
        return receiverSpan.Length;
    }

    /// <summary>
    /// Decodes the address to its raw encoding.
    /// </summary>
    /// <param name="humanReadablePart">Receives the human-readable part of the address (e.g. "zs" or "ztestsapling").</param>
    /// <param name="data">Receives the raw encoding of the data within the address.</param>
    /// <returns>The actual length of the decoded bytes written to <paramref name="humanReadablePart"/> and <paramref name="data"/>.</returns>
    /// <exception cref="FormatException">Thrown if the address is invalid.</exception>
    internal (int HumanReadablePartLength, int DataLength) Decode(Span<char> humanReadablePart, Span<byte> data) => Bech32.Original.Decode(this.Address, humanReadablePart, data);

    /// <inheritdoc/>
    protected override bool CheckValidity(bool throwIfInvalid = false)
    {
        (int Tag, int Data)? length = Bech32.GetDecodedLength(this.Address);
        if (length is null)
        {
            return false;
        }

        Span<char> tag = stackalloc char[length.Value.Tag];
        Span<byte> data = stackalloc byte[length.Value.Data];
        return Bech32.Original.TryDecode(this.Address, tag, data, out _, out _, out _);
    }

    private static string CreateAddress(SaplingReceiver receiver, ZcashNetwork network)
    {
        string humanReadablePart = network switch
        {
            ZcashNetwork.MainNet => MainNetHumanReadablePart,
            ZcashNetwork.TestNet => TestNetHumanReadablePart,
            _ => throw new NotSupportedException("Unrecognized network."),
        };
        Span<byte> receiverSpan = receiver.GetSpan();
        Span<char> addressChars = stackalloc char[Bech32.GetEncodedLength(humanReadablePart.Length, receiverSpan.Length)];
        int charsLength = Bech32.Original.Encode(humanReadablePart, receiverSpan, addressChars);
        return addressChars.Slice(0, charsLength).ToString();
    }
}
