namespace San9.Client.Godot;

public static class ClientLayout
{
	public const int LogicalWidth = 1024;
	public const int LogicalHeight = 768;

	public static LogicalCanvasTransform FitToWindow(int windowWidth, int windowHeight)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowWidth);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowHeight);

		double scale = Math.Min(
			(double)windowWidth / LogicalWidth,
			(double)windowHeight / LogicalHeight);
		double contentWidth = LogicalWidth * scale;
		double contentHeight = LogicalHeight * scale;

		return new LogicalCanvasTransform(
			scale,
			new CanvasRectangle(
				(windowWidth - contentWidth) / 2,
				(windowHeight - contentHeight) / 2,
				contentWidth,
				contentHeight));
	}
}

public readonly record struct CanvasPoint(double X, double Y);

public readonly record struct CanvasRectangle(
	double X,
	double Y,
	double Width,
	double Height)
{
	public bool Contains(CanvasPoint point) =>
		point.X >= X &&
		point.Y >= Y &&
		point.X < X + Width &&
		point.Y < Y + Height;
}

public readonly record struct LogicalCanvasTransform(
	double Scale,
	CanvasRectangle ContentRectangle)
{
	public CanvasPoint LogicalToWindow(CanvasPoint point) => new(
		ContentRectangle.X + (point.X * Scale),
		ContentRectangle.Y + (point.Y * Scale));

	public bool TryWindowToLogical(CanvasPoint point, out CanvasPoint logicalPoint)
	{
		if (!ContentRectangle.Contains(point))
		{
			logicalPoint = default;
			return false;
		}

		logicalPoint = new CanvasPoint(
			(point.X - ContentRectangle.X) / Scale,
			(point.Y - ContentRectangle.Y) / Scale);
		return true;
	}
}
