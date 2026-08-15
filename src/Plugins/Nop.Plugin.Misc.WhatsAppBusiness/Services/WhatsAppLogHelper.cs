using System.Linq;

namespace Nop.Plugin.Misc.WhatsAppBusiness.Services;

public static class WhatsAppLogHelper
{
	public static string MaskPhone(string phone)
	{
		if (string.IsNullOrWhiteSpace(phone))
		{
			return "***";
		}
		string text = phone.Trim();
		bool flag = text.StartsWith('+');
		string text2 = new string(text.Where(char.IsDigit).ToArray());
		if (text2.Length < 4)
		{
			return "***";
		}
		string text3 = (flag ? "+" : string.Empty) + text2.Substring(0, 2);
		string text4 = text2.Substring(text2.Length - 2);
		return text3 + "***" + text4;
	}
}
