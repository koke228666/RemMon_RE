using System.Drawing;
using System.Windows.Forms;

namespace RemMon;

internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
	private static readonly Color MenuBackground = Color.FromArgb(7, 17, 27);

	private static readonly Color ItemHover = Color.FromArgb(19, 38, 61);

	private static readonly Color Border = Color.FromArgb(49, 69, 95);

	private static readonly Color Separator = Color.FromArgb(55, 255, 255, 255);

	public TrayMenuRenderer()
		: base(new TrayMenuColorTable())
	{
		base.RoundedEdges = true;
	}

	protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
	{
		using SolidBrush brush = new SolidBrush(MenuBackground);
		e.Graphics.FillRectangle(brush, e.AffectedBounds);
	}

	protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
	{
		using Pen pen = new Pen(Border);
		Rectangle rect = new Rectangle(Point.Empty, e.ToolStrip.Size);
		rect.Width--;
		rect.Height--;
		e.Graphics.DrawRectangle(pen, rect);
	}

	protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
	{
		if (!e.Item.Selected)
		{
			return;
		}
		Rectangle rect = new Rectangle(4, 2, e.Item.Width - 8, e.Item.Height - 4);
		using SolidBrush brush = new SolidBrush(ItemHover);
		e.Graphics.FillRectangle(brush, rect);
	}

	protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
	{
		using Pen pen = new Pen(Separator);
		int num = e.Item.Height / 2;
		e.Graphics.DrawLine(pen, 10, num, e.Item.Width - 10, num);
	}
}
