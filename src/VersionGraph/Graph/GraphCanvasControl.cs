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
    private const double BranchLineHeight = 16;
    private const double LaneWidth = 22;
    private const double NodeRadius = 5;
    private const double LeftPadding = 14;
    private const double TextGap = 12;
    private const double TextWidth = 640;

    // 어두운 배경 위에서도 또렷하게 보이도록 채도/명도를 높인 네온 팔레트
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x39, 0xFF, 0x14), Color.FromRgb(0x00, 0xE5, 0xFF),
        Color.FromRgb(0xFF, 0x2E, 0x63), Color.FromRgb(0xFF, 0xC1, 0x07),
        Color.FromRgb(0x7B, 0x61, 0xFF), Color.FromRgb(0x00, 0xFF, 0xA6),
        Color.FromRgb(0xFF, 0x6E, 0x27), Color.FromRgb(0x4E, 0x9A, 0xE8)
    ];

    private static readonly Brush RowTextBrush = FreezeBrush(new SolidColorBrush(Color.FromRgb(0xC9, 0xFF, 0xC9)));

    private static Brush FreezeBrush(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

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
        double height = 0;
        foreach (var commit in graph.Commits)
            height += RowHeightFor(commit);
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

        // 브랜치가 있는 커밋은 2줄이라 행 높이가 다르므로, 고정 간격 대신
        // 커밋마다 누적한 실제 중심 Y좌표를 미리 계산해 노드/엣지에 그대로 쓴다
        var rowCenterY = ComputeRowCenters(graph);

        // 먼저 엣지(선)를 전부 그리고 그 위에 노드를 그려야 선이 노드에 가려지지 않는다
        for (var i = 0; i < graph.Commits.Count; i++)
        {
            var commit = graph.Commits[i];
            var fromY = rowCenterY[i];

            foreach (var edge in commit.Edges)
            {
                if (!indexBySha.TryGetValue(edge.ParentSha, out var parentIndex))
                    continue; // 히스토리 경계(shallow) 밖의 부모

                var toY = rowCenterY[parentIndex];
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
            var y = rowCenterY[i];
            var brush = new SolidColorBrush(ColorFor(commit.ColorIndex));

            dc.DrawEllipse(brush, null, new Point(x, y), NodeRadius, NodeRadius);

            var textX = LeftPadding + graph.LaneCount * LaneWidth + TextGap;
            DrawRowText(dc, commit, textX, y);
        }
    }

    // 브랜치가 달린 커밋은 1번째 줄에 브랜치명, 2번째 줄에 커밋 내용을 나눠 그린다.
    // 브랜치가 없으면 굳이 빈 줄을 만들지 않고 커밋 내용만 한 줄로 그린다.
    private void DrawRowText(DrawingContext dc, CommitNode commit, double x, double centerY)
    {
        var contentText = MakeFormattedText($"{commit.Message}  —  {commit.AuthorName}, {commit.ShortSha}");

        if (commit.RefLabels.Count == 0)
        {
            dc.DrawText(contentText, new Point(x, centerY - contentText.Height / 2));
            return;
        }

        var branchText = MakeFormattedText($"[{string.Join(", ", commit.RefLabels)}]");
        var top = centerY - (branchText.Height + contentText.Height) / 2;
        dc.DrawText(branchText, new Point(x, top));
        dc.DrawText(contentText, new Point(x, top + branchText.Height));
    }

    private FormattedText MakeFormattedText(string text) => new(
        text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        _typeface, 13, RowTextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
    {
        MaxTextWidth = TextWidth,
        MaxLineCount = 1,
        Trimming = TextTrimming.CharacterEllipsis
    };

    private static bool HasBranchLine(CommitNode commit) => commit.RefLabels.Count > 0;
    private static double RowHeightFor(CommitNode commit) => HasBranchLine(commit) ? RowHeight + BranchLineHeight : RowHeight;

    // 커밋마다 행 높이가 달라(브랜치 있으면 2줄) 고정 간격을 쓸 수 없어서
    // 누적 Y를 미리 계산해두고 노드/엣지/텍스트가 전부 이 값을 공유해서 쓴다
    private static double[] ComputeRowCenters(GraphModel graph)
    {
        var centers = new double[graph.Commits.Count];
        var y = 0.0;
        for (var i = 0; i < graph.Commits.Count; i++)
        {
            var height = RowHeightFor(graph.Commits[i]);
            centers[i] = y + height / 2;
            y += height;
        }
        return centers;
    }

    private static double LaneCenterX(int lane) => LeftPadding + lane * LaneWidth + LaneWidth / 2;
    private static Color ColorFor(int colorIndex) => Palette[colorIndex % Palette.Length];
}
