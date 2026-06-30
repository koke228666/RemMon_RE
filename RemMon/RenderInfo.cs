namespace RemMon;

internal sealed class RenderInfo
{
	public static RenderInfo Empty { get; } = new RenderInfo();

	public string? ApiText { get; set; }

	public int? Width { get; set; }

	public int? Height { get; set; }

	public string Text
	{
		get
		{
			string obj = (string.IsNullOrWhiteSpace(ApiText) ? "API: N/A" : ApiText);
			int? width = Width;
			object obj2;
			if (width.HasValue && width.GetValueOrDefault() > 0)
			{
				width = Height;
				if (width.HasValue && width.GetValueOrDefault() > 0)
				{
					obj2 = $"{Width}x{Height}";
					goto IL_008b;
				}
			}
			obj2 = "N/A";
			goto IL_008b;
			IL_008b:
			string text = (string)obj2;
			return obj + "  " + text;
		}
	}
}
