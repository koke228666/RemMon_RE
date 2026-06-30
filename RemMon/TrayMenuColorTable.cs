using System.Drawing;
using System.Windows.Forms;

namespace RemMon;

internal sealed class TrayMenuColorTable : ProfessionalColorTable
{
	public override Color ToolStripDropDownBackground => Color.FromArgb(7, 17, 27);

	public override Color ImageMarginGradientBegin => Color.FromArgb(7, 17, 27);

	public override Color ImageMarginGradientMiddle => Color.FromArgb(7, 17, 27);

	public override Color ImageMarginGradientEnd => Color.FromArgb(7, 17, 27);

	public override Color MenuItemSelected => Color.FromArgb(19, 38, 61);

	public override Color MenuItemBorder => Color.FromArgb(47, 140, 255);
}
