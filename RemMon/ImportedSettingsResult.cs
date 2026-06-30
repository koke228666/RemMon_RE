namespace RemMon;

public sealed record ImportedSettingsResult(OverlaySettings Settings, bool HasExtraProperties, bool HasMissingProperties)
{
	public bool HasSchemaDifferences
	{
		get
		{
			if (!HasExtraProperties)
			{
				return HasMissingProperties;
			}
			return true;
		}
	}
}
