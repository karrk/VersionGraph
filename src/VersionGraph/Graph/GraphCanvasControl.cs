using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VersionGraph.Models;
using Size = System.Windows.Size;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace VersionGraph.Graph;

/// <summary>
/// 레이아웃이 끝난 GraphModel을 직접 OnRender로 그리는 경량 컨트롤.
/// 커밋 수가 많아도(수천 개) Visual Tree를 만들지 않아 가볍다.
/// </summary>
public sealed class GraphCanvasControl : FrameworkElement
{
    private const double RowHeight = 28;
    private const double BranchLineHeight = 20;
    private const double LaneWidth = 22;
    private const double NodeRadius = 5;
    private const double LeftPadding = 14;
    private const double TextGap = 12;
    private const double TextWidth = 800;

    // 어두운 배경 위에서도 또렷하게 보이도록 채도/명도를 높인 네온 팔레트
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x39, 0xFF, 0x14), Color.FromRgb(0x00, 0xE5, 0xFF),
        Color.FromRgb(0xFF, 0x2E, 0x63), Color.FromRgb(0xFF, 0xC1, 0x07),
        Color.FromRgb(0x7B, 0x61, 0xFF), Color.FromRgb(0x00, 0xFF, 0xA6),
        Color.FromRgb(0xFF, 0x6E, 0x27), Color.FromRgb(0x4E, 0x9A, 0xE8)
    ];

    // Palette와 1:1 대응하는 고정 브러시. 노드/엣지/텍스트가 전부 이 브러시를 공유해 브랜치별 색이 통일된다.
    private static readonly Brush[] PaletteBrushes = Palette.Select(c => FreezeBrush(new SolidColorBrush(c))).ToArray();

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

    public static readonly DependencyProperty IsInteractiveProperty = DependencyProperty.Register(
        nameof(IsInteractive), typeof(bool), typeof(GraphCanvasControl),
        new PropertyMetadata(false, OnIsInteractiveChanged));

    /// <summary>true일 때만 커밋 행 클릭/호버가 동작 (전체화면 전용 기능).</summary>
    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    public static readonly DependencyProperty CommitClickCommandProperty = DependencyProperty.Register(
        nameof(CommitClickCommand), typeof(ICommand), typeof(GraphCanvasControl));

    /// <summary>커밋 행 클릭 시 해당 CommitNode를 파라미터로 실행되는 커맨드.</summary>
    public ICommand? CommitClickCommand
    {
        get => (ICommand?)GetValue(CommitClickCommandProperty);
        set => SetValue(CommitClickCommandProperty, value);
    }

    // 호버 중인 행 인덱스. -1 = 없음. OnRender에서 하이라이트를 그리는 데 사용
    private int _hoverIndex = -1;

    private static void OnIsInteractiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 인터랙티브 해제 시 남아있던 호버 상태를 지워 하이라이트 잔상 방지
        var control = (GraphCanvasControl)d;
        control.SetHoverIndex(-1);
        control.Cursor = null;
    }

    private readonly Typeface _typeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    protected override Size MeasureOverride(Size availableSize)
    {
        var graph = Graph;
        var width = LeftPadding + graph.LaneCount * LaneWidth + TextGap + TextWidth;
        double height = 0;
        foreach (var commit in graph.Commits)
            height += RowHeightFor(commit);
        return new Size(width, height);
    }

    // 호버 하이라이트용 반투명 브러시 (팔레트 0번 초록 톤과 맞춤)
    private static readonly Brush HoverBrush = FreezeBrush(new SolidColorBrush(Color.FromArgb(0x22, 0x39, 0xFF, 0x14)));

    // 브랜치 라벨 박스: CRT 톤에 맞춘 녹색기 도는 회색조
    private static readonly Brush LabelBgBrush = FreezeBrush(new SolidColorBrush(Color.FromRgb(0x1C, 0x24, 0x1C)));
    private static readonly Brush LabelTextBrush = FreezeBrush(new SolidColorBrush(Color.FromRgb(0xB4, 0xC0, 0xB4)));
    private static readonly Pen LabelBorderPen = FreezePen(new Pen(new SolidColorBrush(Color.FromRgb(0x52, 0x5E, 0x52)), 1));

    private static Pen FreezePen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var graph = Graph;
        if (graph.Commits.Count == 0)
            return;

        // 빈 영역도 마우스 히트테스트에 걸리도록 투명 배경을 전체에 깔아둔다
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        if (IsInteractive && _hoverIndex >= 0 && _hoverIndex < graph.Commits.Count)
        {
            var top = RowTopY(graph, _hoverIndex);
            dc.DrawRectangle(HoverBrush, null,
                new Rect(0, top, RenderSize.Width, RowHeightFor(graph.Commits[_hoverIndex])));
        }

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
                var pen = new Pen(BrushFor(edge.ColorIndex), 2);

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
            var brush = BrushFor(commit.ColorIndex);

            dc.DrawEllipse(brush, null, new Point(x, y), NodeRadius, NodeRadius);

            var textX = LeftPadding + graph.LaneCount * LaneWidth + TextGap;
            DrawRowText(dc, commit, textX, y);
        }
    }

    // 브랜치가 달린 커밋은 1번째 줄에 브랜치명, 2번째 줄에 커밋 내용을 나눠 그린다.
    // 브랜치가 없으면 굳이 빈 줄을 만들지 않고 커밋 내용만 한 줄로 그린다.
    private void DrawRowText(DrawingContext dc, CommitNode commit, double x, double centerY)
    {
        var brush = BrushFor(commit.ColorIndex);
        // '/'는 포맷 문자열에서 문화권별 날짜 구분자로 치환되므로 따옴표로 감싸 리터럴 고정
        var timestamp = commit.When.ToString("[yy-MM-dd '/' HH:mm:ss]");
        var contentText = MakeFormattedText($"{timestamp} {commit.Message}", brush);

        if (commit.RefLabels.Count == 0)
        {
            dc.DrawText(contentText, new Point(x, centerY - contentText.Height / 2));
            return;
        }

        var top = centerY - (BranchLineHeight + contentText.Height) / 2;
        DrawRefLabelBoxes(dc, commit.RefLabels, x, top);
        dc.DrawText(contentText, new Point(x, top + BranchLineHeight));
    }

    // 브랜치/태그 라벨을 각각 라운딩 박스에 담아 가로로 나열
    private void DrawRefLabelBoxes(DrawingContext dc, IReadOnlyList<string> labels, double x, double top)
    {
        const double padX = 6;
        const double gap = 6;
        var boxHeight = BranchLineHeight - 4;

        foreach (var label in labels)
        {
            var text = MakeFormattedText(label, LabelTextBrush, 12);
            var rect = new Rect(x, top + (BranchLineHeight - boxHeight) / 2, text.Width + padX * 2, boxHeight);
            dc.DrawRoundedRectangle(LabelBgBrush, LabelBorderPen, rect, 4, 4);
            dc.DrawText(text, new Point(rect.X + padX, rect.Y + (boxHeight - text.Height) / 2));
            x += rect.Width + gap;
        }
    }

    private FormattedText MakeFormattedText(string text, Brush brush, double fontSize = 16) => new(
        text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        _typeface, fontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
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
    private static Brush BrushFor(int colorIndex) => PaletteBrushes[colorIndex % PaletteBrushes.Length];

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsInteractive)
            return;

        var index = RowIndexAt(e.GetPosition(this).Y);
        SetHoverIndex(index);
        Cursor = index >= 0 ? Cursors.Hand : null;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SetHoverIndex(-1);
        Cursor = null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsInteractive)
            return;

        var index = RowIndexAt(e.GetPosition(this).Y);
        if (index < 0)
            return;

        var commit = Graph.Commits[index];
        if (CommitClickCommand?.CanExecute(commit) == true)
            CommitClickCommand.Execute(commit);
    }

    private void SetHoverIndex(int index)
    {
        if (_hoverIndex == index)
            return;
        _hoverIndex = index;
        InvalidateVisual();
    }

    // Y좌표를 누적 행 높이로 역산해 커밋 인덱스를 찾는다. 범위 밖이면 -1
    private int RowIndexAt(double y)
    {
        var graph = Graph;
        var top = 0.0;
        for (var i = 0; i < graph.Commits.Count; i++)
        {
            top += RowHeightFor(graph.Commits[i]);
            if (y < top)
                return i;
        }
        return -1;
    }

    private static double RowTopY(GraphModel graph, int index)
    {
        var top = 0.0;
        for (var i = 0; i < index; i++)
            top += RowHeightFor(graph.Commits[i]);
        return top;
    }
}
