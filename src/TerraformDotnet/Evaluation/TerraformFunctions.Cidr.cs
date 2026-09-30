using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using TerraformDotnet.Hcl.Evaluation;

namespace TerraformDotnet.Evaluation;

public sealed partial class TerraformFunctions
{
    /// <summary>An IP address (as an unsigned integer) with its prefix length.</summary>
    private readonly record struct CidrBlock(BigInteger Address, int PrefixLength, int Bits)
    {
        public BigInteger HostCount => BigInteger.One << (Bits - PrefixLength);

        public BigInteger Network => Address & ~(HostCount - 1) & AllBits(Bits);

        public bool IsV6 => Bits == 128;
    }

    private static void AddCidr(Dictionary<string, Entry> table)
    {
        Add(table, "cidrhost", 2, 2, CidrHost);
        Add(table, "cidrnetmask", 1, 1, CidrNetmask);
        Add(table, "cidrsubnet", 3, 3, CidrSubnet);
        Add(table, "cidrsubnets", 1, -1, CidrSubnets);
        Add(table, "cidrcontains", 2, 2, CidrContains);
    }

    private static BigInteger AllBits(int bits) => (BigInteger.One << bits) - 1;

    private static HclValue? CidrHost(FunctionCall c)
    {
        if (ParseBlock(c, c.String(0), requirePrefix: true) is not { } block)
        {
            return null;
        }

        var hostNumber = WholeNumber(c, 1);
        var count = block.HostCount;
        if (hostNumber < 0)
        {
            hostNumber += count;
        }

        if (hostNumber < 0 || hostNumber >= count)
        {
            throw c.Error($"prefix of {block.PrefixLength} does not accommodate a host numbered {c.Number(1).ToString(CultureInfo.InvariantCulture)}.");
        }

        return Str(FormatAddress(block.Network + hostNumber, block.Bits));
    }

    private static HclValue? CidrNetmask(FunctionCall c)
    {
        if (ParseBlock(c, c.String(0), requirePrefix: true) is not { } block)
        {
            return null;
        }

        if (block.IsV6)
        {
            throw c.Error("IPv6 addresses cannot have a netmask: use the prefix length instead.");
        }

        var mask = AllBits(32) ^ (block.HostCount - 1);

        return Str(FormatAddress(mask, 32));
    }

    private static HclValue? CidrSubnet(FunctionCall c)
    {
        if (ParseBlock(c, c.String(0), requirePrefix: true) is not { } block)
        {
            return null;
        }

        var newBits = c.Int(1);
        if (newBits < 0)
        {
            return null;
        }

        var networkNumber = WholeNumber(c, 2);
        if (networkNumber < 0)
        {
            return null;
        }

        var newLength = block.PrefixLength + newBits;
        if (newLength > block.Bits)
        {
            throw c.Error($"insufficient address space to extend prefix of {block.PrefixLength} by {newBits}.");
        }

        if (networkNumber < 0 || networkNumber >= BigInteger.One << newBits)
        {
            throw c.Error($"prefix extension of {newBits} does not accommodate a subnet numbered {c.Number(2).ToString(CultureInfo.InvariantCulture)}.");
        }

        var address = block.Network + (networkNumber << (block.Bits - newLength));

        return Str($"{FormatAddress(address, block.Bits)}/{newLength}");
    }

    private static HclValue? CidrSubnets(FunctionCall c)
    {
        if (ParseBlock(c, c.String(0), requirePrefix: true) is not { } block)
        {
            return null;
        }

        var start = block.Network;
        var end = start + block.HostCount;
        var cursor = start;
        var result = new List<HclValue>();
        for (var i = 1; i < c.Count; i++)
        {
            var newBits = c.AsInt(c.Values[i], $"argument {i + 1}");
            if (newBits < 1)
            {
                throw c.Error($"argument {i + 1} must extend the prefix by at least one bit.");
            }

            var newLength = block.PrefixLength + newBits;
            if (newLength > block.Bits)
            {
                throw c.Error($"argument {i + 1} extends the prefix beyond {block.Bits} bits.");
            }

            var size = BigInteger.One << (block.Bits - newLength);
            var aligned = ((cursor + size - 1) / size) * size;
            if (aligned + size > end)
            {
                throw c.Error($"not enough remaining address space for a subnet with a prefix of {newLength} bits.");
            }

            result.Add(Str($"{FormatAddress(aligned, block.Bits)}/{newLength}"));
            cursor = aligned + size;
        }

        return List(result);
    }

    private static HclValue? CidrContains(FunctionCall c)
    {
        if (ParseBlock(c, c.String(0), requirePrefix: true) is not { } container
            || ParseBlock(c, c.String(1), requirePrefix: false) is not { } contained)
        {
            return null;
        }

        if (container.Bits != contained.Bits)
        {
            return Bool(false);
        }

        var inside = contained.PrefixLength >= container.PrefixLength
            && (contained.Address & ~(container.HostCount - 1) & AllBits(container.Bits)) == container.Network;

        return Bool(inside);
    }

    private static BigInteger WholeNumber(FunctionCall c, int index)
    {
        var number = c.Number(index);
        if (number != Math.Floor(number))
        {
            throw c.Error($"argument {index + 1} must be a whole number.");
        }

        return new BigInteger(number);
    }

    /// <summary>
    /// Parses an address or prefix. Returns <c>null</c> for spellings whose handling differs between
    /// Terraform versions (leading zeros, IPv4-mapped IPv6, zones) so the result stays unknown.
    /// </summary>
    private static CidrBlock? ParseBlock(FunctionCall c, string text, bool requirePrefix)
    {
        var slash = text.IndexOf('/', StringComparison.Ordinal);
        var addressText = slash < 0 ? text : text[..slash];
        if (slash < 0 && requirePrefix)
        {
            throw c.Error($"invalid CIDR expression: no '/' in \"{text}\".");
        }

        int? prefixLength = null;
        if (slash >= 0)
        {
            var lengthText = text[(slash + 1)..];
            if (lengthText.Length == 0 || !lengthText.All(char.IsAsciiDigit) || !int.TryParse(lengthText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                throw c.Error($"invalid CIDR expression: invalid prefix length in \"{text}\".");
            }

            prefixLength = parsed;
        }

        if (addressText.Contains(':', StringComparison.Ordinal))
        {
            if (addressText.Contains('.', StringComparison.Ordinal) || addressText.Contains('%', StringComparison.Ordinal))
            {
                return null;
            }

            if (!IPAddress.TryParse(addressText, out var v6) || v6.AddressFamily != AddressFamily.InterNetworkV6)
            {
                throw c.Error($"invalid CIDR expression: invalid address in \"{text}\".");
            }

            if (v6.IsIPv4MappedToIPv6)
            {
                return null;
            }

            return Build(c, text, new BigInteger(v6.GetAddressBytes(), isUnsigned: true, isBigEndian: true), prefixLength, 128);
        }

        var groups = addressText.Split('.');
        if (groups.Length != 4 || groups.Any(g => g.Length is 0 or > 3 || !g.All(char.IsAsciiDigit)))
        {
            throw c.Error($"invalid CIDR expression: invalid address in \"{text}\".");
        }

        if (groups.Any(g => g.Length > 1 && g[0] == '0'))
        {
            return null;
        }

        var address = BigInteger.Zero;
        foreach (var group in groups)
        {
            var octet = int.Parse(group, CultureInfo.InvariantCulture);
            if (octet > 255)
            {
                throw c.Error($"invalid CIDR expression: invalid address in \"{text}\".");
            }

            address = (address << 8) + octet;
        }

        return Build(c, text, address, prefixLength, 32);
    }

    private static CidrBlock Build(FunctionCall c, string text, BigInteger address, int? prefixLength, int bits)
    {
        if (prefixLength > bits)
        {
            throw c.Error($"invalid CIDR expression: prefix length out of range in \"{text}\".");
        }

        return new CidrBlock(address, prefixLength ?? bits, bits);
    }

    private static string FormatAddress(BigInteger address, int bits)
    {
        var bytes = new byte[bits / 8];
        var raw = address.ToByteArray(isUnsigned: true, isBigEndian: true);
        raw.CopyTo(bytes, bytes.Length - raw.Length);

        return new IPAddress(bytes).ToString();
    }
}
