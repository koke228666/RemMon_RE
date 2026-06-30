using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace RemMon;

public sealed class FrameTimeGraphView : FrameworkElement
{
	private const int ExpectedPointCount = 300;

	private IReadOnlyList<FrameTimeGraphPoint> _points = Array.Empty<FrameTimeGraphPoint>();

	public Brush GraphBackground { get; set; } = Brushes.Black;

	public Brush LineBrush { get; set; } = Brushes.Red;

	public Brush FillBrush { get; set; } = new SolidColorBrush(Color.FromArgb(130, 90, 0, 0));

	public double MaxFrameTimeMs { get; set; } = 50.0;

	public void SetPoints(IReadOnlyList<FrameTimeGraphPoint> points)
	{
		_points = points;
		InvalidateVisual();
	}

	public void Clear()
	{
		_points = Array.Empty<FrameTimeGraphPoint>();
		InvalidateVisual();
	}

	protected override void OnRender(DrawingContext drawingContext)
	{
		base.OnRender(drawingContext);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (actualWidth <= 1.0 || actualHeight <= 1.0)
		{
			return;
		}
		drawingContext.DrawRectangle(GraphBackground, null, new Rect(0.0, 0.0, actualWidth, actualHeight));
		if (_points.Count >= 2)
		{
			double maxFrameTimeMs = Math.Max(1.0, MaxFrameTimeMs);
			List<List<Point>> list = BuildSegments(actualWidth, actualHeight, maxFrameTimeMs);
			if (list.Count != 0)
			{
				drawingContext.PushGuidelineSet(new GuidelineSet(new double[1] { 0.5 }, new double[1] { 0.5 }));
				StreamGeometry geometry = CreateFillGeometry(list, actualHeight);
				drawingContext.DrawGeometry(FillBrush, null, geometry);
				Pen pen = new Pen(LineBrush, 1.0)
				{
					StartLineCap = PenLineCap.Flat,
					EndLineCap = PenLineCap.Flat,
					LineJoin = PenLineJoin.Miter
				};
				pen.Freeze();
				StreamGeometry geometry2 = CreateLineGeometry(list);
				drawingContext.DrawGeometry(null, pen, geometry2);
				drawingContext.Pop();
			}
		}
	}

	private List<List<Point>> BuildSegments(double width, double height, double maxFrameTimeMs)
	{
		List<List<Point>> list = new List<List<Point>>();
		List<Point> list2 = new List<Point>();
		int num = Math.Max(0, 300 - _points.Count);
		int num2 = Math.Max(1, 299);
		for (int i = 0; i < _points.Count; i++)
		{
			double? valueMs = _points[i].ValueMs;
			if (!valueMs.HasValue || !(valueMs.GetValueOrDefault() > 0.0) || double.IsNaN(valueMs.Value) || double.IsInfinity(valueMs.Value))
			{
				AddSegmentIfValid(list, list2);
				list2 = new List<Point>();
				continue;
			}
			double num3 = Math.Clamp(valueMs.Value, 0.0, maxFrameTimeMs);
			double num4 = (double)(num + i) * width / (double)num2;
			double num5 = height - num3 / maxFrameTimeMs * height;
			if (double.IsNaN(num4) || double.IsInfinity(num4) || double.IsNaN(num5) || double.IsInfinity(num5))
			{
				AddSegmentIfValid(list, list2);
				list2 = new List<Point>();
			}
			else
			{
				double x = Math.Clamp(Math.Round(num4) + 0.5, 0.0, width);
				double y = Math.Clamp(Math.Round(num5) + 0.5, 0.0, height);
				list2.Add(new Point(x, y));
			}
		}
		AddSegmentIfValid(list, list2);
		return list;
	}

	private static void AddSegmentIfValid(List<List<Point>> segments, List<Point> segment)
	{
		if (segment.Count >= 2)
		{
			segments.Add(segment);
		}
	}

	private static StreamGeometry CreateLineGeometry(IReadOnlyList<IReadOnlyList<Point>> segments)
	{
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			foreach (IReadOnlyList<Point> segment in segments)
			{
				if (segment.Count >= 2)
				{
					streamGeometryContext.BeginFigure(segment[0], isFilled: false, isClosed: false);
					for (int i = 1; i < segment.Count; i++)
					{
						streamGeometryContext.LineTo(segment[i], isStroked: true, isSmoothJoin: false);
					}
				}
			}
		}
		streamGeometry.Freeze();
		return streamGeometry;
	}

	private static StreamGeometry CreateFillGeometry(IReadOnlyList<IReadOnlyList<Point>> segments, double height)
	{
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			foreach (IReadOnlyList<Point> segment in segments)
			{
				if (segment.Count >= 2)
				{
					Point startPoint = segment[0];
					Point point = segment[segment.Count - 1];
					streamGeometryContext.BeginFigure(startPoint, isFilled: true, isClosed: true);
					for (int i = 1; i < segment.Count; i++)
					{
						streamGeometryContext.LineTo(segment[i], isStroked: false, isSmoothJoin: false);
					}
					streamGeometryContext.LineTo(new Point(point.X, height), isStroked: false, isSmoothJoin: false);
					streamGeometryContext.LineTo(new Point(startPoint.X, height), isStroked: false, isSmoothJoin: false);
				}
			}
		}
		streamGeometry.Freeze();
		return streamGeometry;
	}
}
