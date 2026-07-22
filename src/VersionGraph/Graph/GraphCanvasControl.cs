using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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

    // 엣지마다 매 렌더 new Pen을 만들지 않도록 색상별로 미리 만들어 공유
    private static readonly Pen[] EdgePens = PaletteBrushes.Select(b => FreezePen(new Pen(b, 2))).ToArray();

    private static Brush FreezeBrush(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    public static readonly DependencyProperty GraphProperty = DependencyProperty.Register(
        nameof(Graph), typeof(GraphModel), typeof(GraphCanvasControl),
        new FrameworkPropertyMetadata(GraphModel.Empty,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
            OnGraphChanged));

    private static void OnGraphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 그래프가 교체되면 캐시·인덱스 기반 호버 상태가 전부 무효. 선택 펄스는 새 그래프 기준으로 재계산
        var control = (GraphCanvasControl)d;
        control.RebuildGraphCache();
        control.SetNodeHoverIndex(-1);
        control.SetHoverIndex(-1);
        control.UpdateSelectedPulse();
    }

    // 매 렌더마다 재계산하면 GC 압박이 커서, 그래프가 바뀔 때 한 번만 계산해 보관
    private double[] _rowCenterY = [];
    private Dictionary<string, int> _indexBySha = [];
    // FormattedText 생성(텍스트 셰이핑)이 비싸므로 커밋/라벨 단위로 캐싱
    private readonly Dictionary<string, FormattedText> _contentTextCache = [];
    private readonly Dictionary<string, FormattedText> _labelTextCache = [];

    private void RebuildGraphCache()
    {
        var graph = Graph;
        _rowCenterY = ComputeRowCenters(graph);
        _indexBySha = new Dictionary<string, int>(graph.Commits.Count);
        for (var i = 0; i < graph.Commits.Count; i++)
            _indexBySha[graph.Commits[i].Sha] = i;
        _contentTextCache.Clear();
        _labelTextCache.Clear();
    }

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

    public static readonly DependencyProperty SelectedCommitShaProperty = DependencyProperty.Register(
        nameof(SelectedCommitSha), typeof(string), typeof(GraphCanvasControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((GraphCanvasControl)d).UpdateSelectedPulse()));

    /// <summary>상세 패널에 표시 중인 커밋 SHA. 해당 행에 선택 하이라이트를 그린다.</summary>
    public string? SelectedCommitSha
    {
        get => (string?)GetValue(SelectedCommitShaProperty);
        set => SetValue(SelectedCommitShaProperty, value);
    }

    /// <summary>커밋 행 클릭 시 해당 CommitNode를 파라미터로 실행되는 커맨드.</summary>
    public ICommand? CommitClickCommand
    {
        get => (ICommand?)GetValue(CommitClickCommandProperty);
        set => SetValue(CommitClickCommandProperty, value);
    }

    // 호버 중인 행 인덱스. -1 = 없음. OnRender에서 하이라이트를 그리는 데 사용
    private int _hoverIndex = -1;

    // 노드(동그라미) 호버 상태: 조상 줄기 펄스 애니메이션의 시작점
    private int _nodeHoverIndex = -1;
    private HashSet<string>? _ancestorShas;

    // 선택된 커밋(상세 패널 표시 중)도 호버와 동일한 조상 펄스를 유지
    private int _selectedPulseIndex = -1;
    private HashSet<string>? _selectedPulseShas;

    private DateTime _pulseStart;
    private readonly DispatcherTimer _pulseTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    // 펄스 애니메이션 전용 오버레이 레이어. 타이머 틱마다 이 레이어만 다시 그리므로
    // 본체(텍스트/노드/엣지)의 비싼 재렌더링 없이 저사양 PC에서도 가볍게 돈다
    private readonly DrawingVisual _pulseOverlay = new();

    public GraphCanvasControl()
    {
        AddVisualChild(_pulseOverlay);
        _pulseTimer.Tick += (_, _) => RenderPulseOverlay();
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _pulseOverlay;

    // 호버/선택 어느 쪽이든 활성 상태면 타이머 유지, 둘 다 없으면 정지
    private void UpdatePulseTimer()
    {
        var shouldRun = _ancestorShas is not null || _selectedPulseShas is not null;
        if (shouldRun && !_pulseTimer.IsEnabled)
        {
            _pulseStart = DateTime.UtcNow;
            _pulseTimer.Start();
        }
        else if (!shouldRun && _pulseTimer.IsEnabled)
        {
            _pulseTimer.Stop();
        }
    }

    private void UpdateSelectedPulse()
    {
        _selectedPulseIndex = -1;
        _selectedPulseShas = null;

        var sha = SelectedCommitSha;
        if (sha is not null)
        {
            var commits = Graph.Commits;
            for (var i = 0; i < commits.Count; i++)
            {
                if (commits[i].Sha != sha)
                    continue;
                _selectedPulseIndex = i;
                _selectedPulseShas = CollectAncestorShas(i);
                break;
            }
        }

        UpdatePulseTimer();
        RenderPulseOverlay();
    }

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

    // 선택된 행: 호버보다 진한 배경 + 네온 그린 테두리로 "지금 보고 있는 커밋"을 명확히 구분
    private static readonly Brush SelectedRowBrush = FreezeBrush(new SolidColorBrush(Color.FromArgb(0x3A, 0x39, 0xFF, 0x14)));
    private static readonly Pen SelectedRowPen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0xAA, 0x39, 0xFF, 0x14)), 1));

    // 조상 줄기 오버레이: 은은한 상시 발광 + 흘러가는 펄스 (CRT 신호 흐름 톤의 화이트그린)
    private static readonly Pen AncestorGlowPen = FreezePen(new Pen(new SolidColorBrush(Color.FromArgb(0x82, 0xE8, 0xFF, 0xEA)), 4));
    private static readonly Brush PulseBrush = FreezeBrush(new SolidColorBrush(Color.FromArgb(0xFF, 0xF2, 0xFF, 0xF4)));
    private static readonly Brush NodeHaloBrush = FreezeBrush(new SolidColorBrush(Color.FromArgb(0x8C, 0xE8, 0xFF, 0xEA)));

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

        // 선택 중인 커밋 행 표시 (상세 패널과 연동)
        if (SelectedCommitSha is not null)
        {
            for (var i = 0; i < graph.Commits.Count; i++)
            {
                if (graph.Commits[i].Sha != SelectedCommitSha)
                    continue;
                var top = RowTopY(graph, i);
                dc.DrawRectangle(SelectedRowBrush, SelectedRowPen,
                    new Rect(1, top + 1, RenderSize.Width - 2, RowHeightFor(graph.Commits[i]) - 2));
                break;
            }
        }

        // 먼저 엣지(선)를 전부 그리고 그 위에 노드를 그려야 선이 노드에 가려지지 않는다
        for (var i = 0; i < graph.Commits.Count; i++)
        {
            var commit = graph.Commits[i];
            var fromY = _rowCenterY[i];

            foreach (var edge in commit.Edges)
            {
                if (!_indexBySha.TryGetValue(edge.ParentSha, out var parentIndex))
                    continue; // 히스토리 경계(shallow) 밖의 부모

                var geometry = BuildEdgePath(
                    LaneCenterX(edge.FromLane), fromY, LaneCenterX(edge.ToLane), _rowCenterY[parentIndex]);
                dc.DrawGeometry(null, EdgePens[edge.ColorIndex % EdgePens.Length], geometry);
            }
        }

        for (var i = 0; i < graph.Commits.Count; i++)
        {
            var commit = graph.Commits[i];
            var x = LaneCenterX(commit.Lane);
            var y = _rowCenterY[i];
            var brush = BrushFor(commit.ColorIndex);

            dc.DrawEllipse(brush, null, new Point(x, y), NodeRadius, NodeRadius);

            var textX = LeftPadding + graph.LaneCount * LaneWidth + TextGap;
            DrawRowText(dc, commit, textX, y);
        }
    }

    private void DrawNodeHalo(DrawingContext dc, GraphModel graph, int index)
    {
        if (index < 0 || index >= graph.Commits.Count)
            return;
        var commit = graph.Commits[index];
        var center = new Point(LaneCenterX(commit.Lane), _rowCenterY[index]);
        dc.DrawEllipse(NodeHaloBrush, null, center, NodeRadius + 5, NodeRadius + 5);
    }

    // 같은 레인이면 직선, 레인 이동이면 베지어 곡선. 항상 자식(위)에서 시작해
    // 부모(아래)로 끝나므로 대시 오프셋 애니메이션의 흐름 방향이 자연히 아래를 향한다
    private static Geometry BuildEdgePath(double fromX, double fromY, double toX, double toY)
    {
        if (fromX == toX)
            return new LineGeometry(new Point(fromX, fromY), new Point(toX, toY));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(fromX, fromY), false, false);
            var midY = (fromY + toY) / 2;
            ctx.BezierTo(
                new Point(fromX, midY), new Point(toX, midY), new Point(toX, toY),
                true, false);
        }
        return geometry;
    }

    // 호버/선택 노드의 모든 조상 엣지 위에 발광 오버레이 + 아래로 흘러가는 대시 펄스를 오버레이 레이어에 그린다.
    // 본체 OnRender와 분리되어 있어 타이머 틱마다 이 메서드만 실행된다
    private void RenderPulseOverlay()
    {
        using var dc = _pulseOverlay.RenderOpen();

        var graph = Graph;
        if (graph.Commits.Count == 0 || (_ancestorShas is null && _selectedPulseShas is null))
            return; // 비우기만 하고 종료 (오버레이 클리어)

        // 대시 오프셋을 시간에 따라 줄이면 대시 조각이 경로 진행 방향(아래)으로 이동해 보인다
        var elapsed = (DateTime.UtcNow - _pulseStart).TotalSeconds;
        var pulsePen = new Pen(PulseBrush, 3)
        {
            DashStyle = new DashStyle([2, 4], -elapsed * 10 % 6),
            DashCap = PenLineCap.Round
        };

        for (var i = 0; i < graph.Commits.Count; i++)
        {
            var commit = graph.Commits[i];
            // 호버/선택 어느 쪽 조상 집합에도 속하지 않으면 스킵 (겹치는 엣지는 한 번만 그림)
            var inHover = _ancestorShas?.Contains(commit.Sha) == true;
            var inSelected = _selectedPulseShas?.Contains(commit.Sha) == true;
            if (!inHover && !inSelected)
                continue;

            foreach (var edge in commit.Edges)
            {
                if (!_indexBySha.TryGetValue(edge.ParentSha, out var parentIndex))
                    continue;

                var geometry = BuildEdgePath(
                    LaneCenterX(edge.FromLane), _rowCenterY[i], LaneCenterX(edge.ToLane), _rowCenterY[parentIndex]);
                dc.DrawGeometry(null, AncestorGlowPen, geometry);
                dc.DrawGeometry(null, pulsePen, geometry);
            }
        }

        // 호버/선택 노드 글로우: 펄스의 시작점 표시
        DrawNodeHalo(dc, graph, _nodeHoverIndex);
        if (_selectedPulseIndex != _nodeHoverIndex)
            DrawNodeHalo(dc, graph, _selectedPulseIndex);
    }

    // 브랜치가 달린 커밋은 1번째 줄에 브랜치명, 2번째 줄에 커밋 내용을 나눠 그린다.
    // 브랜치가 없으면 굳이 빈 줄을 만들지 않고 커밋 내용만 한 줄로 그린다.
    private void DrawRowText(DrawingContext dc, CommitNode commit, double x, double centerY)
    {
        var contentText = ContentTextFor(commit);

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
            var text = LabelTextFor(label);
            var rect = new Rect(x, top + (BranchLineHeight - boxHeight) / 2, text.Width + padX * 2, boxHeight);
            dc.DrawRoundedRectangle(LabelBgBrush, LabelBorderPen, rect, 4, 4);
            dc.DrawText(text, new Point(rect.X + padX, rect.Y + (boxHeight - text.Height) / 2));
            x += rect.Width + gap;
        }
    }

    // 커밋 내용/라벨 텍스트는 그래프가 바뀌기 전까지 불변이므로 캐싱해 재사용.
    // 텍스트 셰이핑 비용이 커서 행 호버 재렌더 등에서 큰 차이가 난다
    private FormattedText ContentTextFor(CommitNode commit)
    {
        if (_contentTextCache.TryGetValue(commit.Sha, out var cached))
            return cached;

        // '/'는 포맷 문자열에서 문화권별 날짜 구분자로 치환되므로 따옴표로 감싸 리터럴 고정
        var timestamp = commit.When.ToString("[yy-MM-dd '/' HH:mm:ss]");
        var text = MakeFormattedText($"{timestamp} {commit.Message}", BrushFor(commit.ColorIndex));
        _contentTextCache[commit.Sha] = text;
        return text;
    }

    private FormattedText LabelTextFor(string label)
    {
        if (_labelTextCache.TryGetValue(label, out var cached))
            return cached;

        var text = MakeFormattedText(label, LabelTextBrush, 12);
        _labelTextCache[label] = text;
        return text;
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
        var pos = e.GetPosition(this);
        var rowIndex = RowIndexAt(pos.Y);

        // 노드(동그라미) 히트테스트는 화면 모드와 무관하게 항상 동작
        SetNodeHoverIndex(NodeIndexAt(pos, rowIndex));

        if (!IsInteractive)
            return;

        SetHoverIndex(rowIndex);
        Cursor = rowIndex >= 0 ? Cursors.Hand : null;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SetNodeHoverIndex(-1);
        SetHoverIndex(-1);
        Cursor = null;
    }

    // 커서가 해당 행 커밋의 노드 원 위에 있는지 판정 (반경 + 약간의 여유)
    private int NodeIndexAt(Point pos, int rowIndex)
    {
        if (rowIndex < 0)
            return -1;

        var commit = Graph.Commits[rowIndex];
        var cx = LaneCenterX(commit.Lane);
        var cy = RowTopY(Graph, rowIndex) + RowHeightFor(commit) / 2;
        var dx = pos.X - cx;
        var dy = pos.Y - cy;
        const double hitRadius = NodeRadius + 4;
        return dx * dx + dy * dy <= hitRadius * hitRadius ? rowIndex : -1;
    }

    private void SetNodeHoverIndex(int index)
    {
        if (_nodeHoverIndex == index)
            return;
        _nodeHoverIndex = index;

        _ancestorShas = index >= 0 ? CollectAncestorShas(index) : null;
        UpdatePulseTimer();
        RenderPulseOverlay();
    }

    // 호버 커밋에서 부모를 따라 도달 가능한 모든 조상(머지의 양쪽 갈래 포함)을 수집
    private HashSet<string> CollectAncestorShas(int startIndex)
    {
        var graph = Graph;
        var bySha = new Dictionary<string, CommitNode>(graph.Commits.Count);
        foreach (var commit in graph.Commits)
            bySha[commit.Sha] = commit;

        var visited = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(graph.Commits[startIndex].Sha);
        while (stack.Count > 0)
        {
            var sha = stack.Pop();
            if (!visited.Add(sha) || !bySha.TryGetValue(sha, out var node))
                continue;
            foreach (var parentSha in node.ParentShas)
                stack.Push(parentSha);
        }
        return visited;
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
