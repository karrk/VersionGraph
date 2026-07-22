using System.Windows;
using System.Windows.Media;
using VersionGraph.Models;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;

namespace VersionGraph.Graph;

/// <summary>
/// 레이아웃이 끝난 GraphModel을 직접 OnRender로 그리는 경량 컨트롤.
/// 커밋 수가 많아도(수천 개) Visual Tree를 만들지 않아 가볍다.
/// </summary>
public sealed class GraphCanvasControl : FrameworkElement
{
    private const double RowHeight = 28;
    private const double LaneWidth = 22;
    private const double NodeRadius = 5;
    private const double LeftPadding = 14;
    private const double TextGap = 12;
    private const double TextWidth = 640;

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x4E, 0x9A, 0xE8), Color.FromRgb(0xE0, 0x6C, 0x4E),
        Color.FromRgb(0x5C, 0xB8, 0x5C), Color.FromRgb(0xC9, 0x7A, 0xE0),
        Color.FromRgb(0xE0, 0xB8, 0x4E), Color.FromRgb(0x4E, 0xC9, 0xC0),
        Color.FromRgb(0xD1, 0x5C, 0x8A), Color.FromRgb(0x8A, 0x9A, 0xE0)
    ];

    public static readonly DependencyProperty GraphProperty = DependencyProperty.Register(
        nameof(Graph), typeof(GraphModel), typeof(GraphCanvasControl),
        new FrameworkPropertyMetadata(GraphModel.Empty, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public GraphModel Graph
    {
        get => (GraphModel)GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    private readonly Typeface _typeface = new("Segoe UI");

    protected override Size MeasureOverride(Size availableSize)
    {
        var graph = Graph;
        var width = LeftPadding + graph.LaneCount * LaneWidth + TextGap + TextWidth;
        var height = graph.Commits.Count * RowHeight;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var graph = Graph;
        if (graph.Commits.Count == 0)
            return;

        var indexBySha = new Dictionary<string, int>(graph.Commits.Count);
        for (var i = 0; i < graph.Commits.Count; i++)
            indexBySha[graph.Commits[i].Sha] = i;

        // 먼저 엣지(선)를 전부 그리고 그 위에 노드를 그려야 선이 노드에 가려지지 않는다
        for (var i = 0; i < graph.Commits.Count; i++)
        {
            var commit = graph.Commits[i];
            var fromY = LaneCenterY(i);

            foreach (var edge in commit.Edges)
            {
                if (!indexBySha.TryGetValue(edge.ParentSha, out var parentIndex))
                    continue; // 히스토리 경계(shallow) 밖의 부모

                var toY = LaneCenterY(parentIndex);
                var fromX = LaneCenterX(edge.FromLane);
                var toX = LaneCenterX(edge.ToLane);
                var pen = new Pen(new SolidColorBrush(ColorFor(edge.ColorIndex)), 2);

                if (edge.FromLane == edge.ToLane)
                {
                    dc.DrawLine(pen, new Point(fromX, fromY), new Point(toX, toY));
                }
                else
                {
                    // 레인 이동은 직선 대신 베지어 곡선으로 부드럽게 표현
                    var geometry = new StreamGeometry();
                    using (var ctx = geometry.Open())
                    {
                        ctx.BeginFigure(new Point(fromX, fromY), false, false);
                        var midY = (fromY + toY) / 2;
                        ctx.BezierTo(
                            new Point(fromX, midY), new Point(toX, midY), new Point(toX, toY),
                            true, false);
                    }
                    dc.DrawGeometry(null, pen, geometry);
                }
            }
        }

        for (var i = 0; i < graph.Commits.Count; i++)
        {
            var commit = graph.Commits[i];
            var x = LaneCenterX(commit.Lane);
            var y = LaneCenterY(i);
            var brush = new SolidColorBrush(ColorFor(commit.ColorIndex));

            dc.DrawEllipse(brush, null, new Point(x, y), NodeRadius, NodeRadius);

            var textX = LeftPadding + graph.LaneCount * LaneWidth + TextGap;
            DrawRowText(dc, commit, textX, y);
        }
    }

    private void DrawRowText(DrawingContext dc, CommitNode commit, double x, double centerY)
    {
        var refLabel = commit.RefLabels.Count > 0 ? $"[{string.Join(", ", commit.RefLabels)}] " : "";
        var text = $"{refLabel}{commit.Message}  —  {commit.AuthorName}, {commit.ShortSha}";

        var formatted = new FormattedText(
            text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            _typeface, 13, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = TextWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

        dc.DrawText(formatted, new Point(x, centerY - formatted.Height / 2));
    }

    private static double LaneCenterX(int lane) => LeftPadding + lane * LaneWidth + LaneWidth / 2;
    private static double LaneCenterY(int rowIndex) => rowIndex * RowHeight + RowHeight / 2;
    private static Color ColorFor(int colorIndex) => Palette[colorIndex % Palette.Length];
}
