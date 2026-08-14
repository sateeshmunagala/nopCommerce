using System;
using System.Linq;

namespace SplatDev.Nop.Plugin.Misc.WhatsAppBusiness.Services;

public static class PiiMask
{
	public static string Cpf(string? v)
	{
		string text = Digits(v);
		if (text.Length != 11)
		{
			return "***";
		}
		return text.Substring(0, 3) + ".***.***-**";
	}

	public static string Cnpj(string? v)
	{
		string text = Digits(v);
		if (text.Length != 14)
		{
			return "***";
		}
		return text.Substring(0, 2) + ".***.***/****-**";
	}

	public static string Card(string? v)
	{
		string text = Digits(v);
		if (text.Length < 4)
		{
			return "****";
		}
		return "****" + text.Substring(text.Length - 4);
	}

	public static string Token(string? v)
	{
		if (!string.IsNullOrEmpty(v))
		{
			return $"{v.Substring(0, Math.Min(4, v.Length))}…(len {v.Length})";
		}
		return "***";
	}

	private static string Digits(string? v)
	{
		return new string((v ?? string.Empty).Where(char.IsDigit).ToArray());
	}
}
