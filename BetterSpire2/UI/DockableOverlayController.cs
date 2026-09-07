#nullable enable
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using System;

namespace BetterSpire2.UI;

/// <summary>
/// Shared floating-panel behavior: title-bar dragging, eight-direction resizing,
/// edge snapping, viewport clamping and normalized persistence.
/// </summary>
internal sealed class DockableOverlayController : IDisposable
{
    [Flags]
    private enum ResizeEdge
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8
    }

    private readonly Control _panel;
    private readonly Control _dragHandle;
    private readonly Viewport _viewport;
    private readonly NGame? _game;
    private readonly Func<OverlayLayoutState> _loadLayout;
    private readonly Action<OverlayLayoutState> _saveLayout;
    private readonly Func<Vector2, bool>? _isInteractionExcluded;
    private readonly bool _canResize;
    private readonly bool _handleOnly;
    private readonly float _viewportMargin;
    private readonly float _resizeMargin;
    private readonly float _snapDistance;

    private Vector2 _minimumSize;
    private Vector2 _maximumSize;
    private Vector2 _preferredSize;
    private Rect2 _lastSafeRect;
    private bool _dragging;
    private bool _resizing;
    private ResizeEdge _resizeEdge;
    private Vector2 _pointerStart;
    private Rect2 _panelStartRect;
    private bool _disposed;

    public DockableOverlayController(
        Control panel,
        Control dragHandle,
        Viewport viewport,
        NGame? game,
        Vector2 minimumSize,
        Vector2 maximumSize,
        bool canResize,
        Func<OverlayLayoutState> loadLayout,
        Action<OverlayLayoutState> saveLayout,
        Func<Vector2, bool>? isInteractionExcluded = null,
        float viewportMargin = 16f,
        float resizeMargin = 8f,
        float snapDistance = 12f,
        bool handleOnly = false)
    {
        _panel = panel;
        _dragHandle = dragHandle;
        _viewport = viewport;
        _game = game;
        _minimumSize = minimumSize;
        _maximumSize = maximumSize;
        _canResize = canResize;
        _handleOnly = handleOnly;
        _loadLayout = loadLayout;
        _saveLayout = saveLayout;
        _isInteractionExcluded = isInteractionExcluded;
        _viewportMargin = viewportMargin;
        _resizeMargin = resizeMargin;
        _snapDistance = snapDistance;
    }

    public bool IsInteracting => _dragging || _resizing;

    public void Initialize(Vector2 defaultSize, Func<Rect2, Vector2, Vector2> defaultPosition)
    {
        if (!UiHelpers.IsValid(_panel) || !UiHelpers.IsValid(_viewport))
        {
            return;
        }

        _lastSafeRect = GetSafeRect();
        OverlayLayoutState layout = _loadLayout();
        _preferredSize = SanitizePreferredSize(layout.HasSize ? layout.Size : defaultSize);
        Vector2 requestedSize = ClampSize(_preferredSize, _lastSafeRect);
        Vector2 size = ApplyPanelSize(requestedSize);

        Vector2 position;
        if (!layout.HasPosition)
        {
            position = defaultPosition(_lastSafeRect, size);
        }
        else if (layout.PositionIsNormalized)
        {
            position = DenormalizePosition(layout.Position, _lastSafeRect, size);
        }
        else
        {
            position = layout.Position;
        }

        _panel.Position = ClampAndSnapPosition(position, size, _lastSafeRect, snap: false);
        _dragHandle.MouseDefaultCursorShape = Control.CursorShape.Move;

        _viewport.SizeChanged += OnViewportChanged;
        if (UiHelpers.IsValid(_game))
        {
            _game!.WindowChange += OnViewportChanged;
        }

        // Migrate old absolute-pixel positions on first display.
        if (layout.HasPosition && !layout.PositionIsNormalized)
        {
            Persist();
        }
    }

    public bool HandleInput(InputEvent inputEvent)
    {
        if (_disposed || !UiHelpers.IsValid(_panel))
        {
            return false;
        }

        if (inputEvent is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            return HandleMouseButton(mouseButton);
        }

        if (inputEvent is not InputEventMouseMotion mouseMotion)
        {
            return false;
        }

        // A release can be lost when the pointer leaves the game window. Stop
        // capture on the first motion event that no longer carries the left mask.
        if ((_dragging || _resizing) && (mouseMotion.ButtonMask & MouseButtonMask.Left) == 0)
        {
            Persist();
            CancelInteraction();
            return true;
        }

        if (_resizing)
        {
            ResizeTo(mouseMotion.Position);
            return true;
        }

        if (_dragging)
        {
            DragTo(mouseMotion.Position);
            return true;
        }

        Rect2 panelRect = _panel.GetGlobalRect();
        if (IsPointInInteractionArea(mouseMotion.Position))
        {
            UpdateResizeCursor(mouseMotion.Position, panelRect);
            return true;
        }

        _panel.MouseDefaultCursorShape = Control.CursorShape.Arrow;
        return false;
    }

    public bool ContainsPoint(Vector2 point)
    {
        return UiHelpers.IsValid(_panel) && _panel.GetGlobalRect().HasPoint(point);
    }

    public bool IsPointInInteractionArea(Vector2 point)
    {
        if (!UiHelpers.IsValid(_panel))
        {
            return false;
        }

        Rect2 rect = _panel.GetGlobalRect();
        if (!_canResize)
        {
            return _handleOnly
                ? UiHelpers.IsValid(_dragHandle) && _dragHandle.GetGlobalRect().HasPoint(point)
                : rect.HasPoint(point);
        }

        return new Rect2(
            rect.Position - new Vector2(_resizeMargin, _resizeMargin),
            rect.Size + new Vector2(_resizeMargin * 2f, _resizeMargin * 2f)).HasPoint(point);
    }

    public void SetNormalizedPosition(Vector2 position)
    {
        if (!UiHelpers.IsValid(_panel) || IsInteracting) return;
        Rect2 safe = GetSafeRect();
        _panel.Position = ClampAndSnapPosition(DenormalizePosition(position, safe, _panel.Size), _panel.Size, safe, snap: false);
        _lastSafeRect = safe;
    }

    public void ResetPointerCursor()
    {
        if (!_dragging && !_resizing && UiHelpers.IsValid(_panel))
        {
            _panel.MouseDefaultCursorShape = Control.CursorShape.Arrow;
        }
    }

    public void ApplyGeometry(
        Vector2 minimumSize,
        Vector2 maximumSize,
        Vector2 preferredSize,
        bool preserveRelativePosition = true)
    {
        if (!UiHelpers.IsValid(_panel))
        {
            return;
        }

        CancelInteraction();
        Rect2 safeRect = GetSafeRect();
        Rect2 previousSafeRect = _lastSafeRect.Size == Vector2.Zero ? safeRect : _lastSafeRect;
        Vector2 normalized = NormalizePosition(_panel.Position, _panel.Size, previousSafeRect);
        _minimumSize = minimumSize;
        _maximumSize = maximumSize;
        _preferredSize = SanitizePreferredSize(preferredSize);
        Vector2 requestedSize = ClampSize(_preferredSize, safeRect);
        Vector2 actualSize = ApplyPanelSize(requestedSize);
        CaptureActualSizeWhenLayoutOverridesRequest(requestedSize, actualSize);
        _panel.Position = preserveRelativePosition
            ? ClampAndSnapPosition(DenormalizePosition(normalized, safeRect, actualSize), actualSize, safeRect, snap: false)
            : ClampAndSnapPosition(_panel.Position, actualSize, safeRect, snap: false);
        _lastSafeRect = safeRect;
    }

    public void ClampToViewport(bool preserveRelativePosition)
    {
        if (!UiHelpers.IsValid(_panel))
        {
            return;
        }

        Rect2 previousSafeRect = _lastSafeRect.Size == Vector2.Zero ? GetSafeRect() : _lastSafeRect;
        Vector2 normalized = NormalizePosition(_panel.Position, _panel.Size, previousSafeRect);
        Rect2 safeRect = GetSafeRect();
        Vector2 requestedSize = ClampSize(GetPreferredSizeFallback(), safeRect);
        Vector2 size = ApplyPanelSize(requestedSize);
        CaptureActualSizeWhenLayoutOverridesRequest(requestedSize, size);
        Vector2 position = preserveRelativePosition
            ? DenormalizePosition(normalized, safeRect, size)
            : _panel.Position;
        _panel.Position = ClampAndSnapPosition(position, size, safeRect, snap: false);
        _lastSafeRect = safeRect;
    }

    public void ResetLayout(Vector2 defaultSize, Func<Rect2, Vector2, Vector2> defaultPosition)
    {
        if (!UiHelpers.IsValid(_panel))
        {
            return;
        }

        CancelInteraction();
        Rect2 safeRect = GetSafeRect();
        _preferredSize = SanitizePreferredSize(defaultSize);
        Vector2 requestedSize = ClampSize(_preferredSize, safeRect);
        Vector2 size = ApplyPanelSize(requestedSize);
        CaptureActualSizeWhenLayoutOverridesRequest(requestedSize, size);
        _panel.Position = ClampAndSnapPosition(defaultPosition(safeRect, size), size, safeRect, snap: false);
        _lastSafeRect = safeRect;
        Persist();
    }

    public void Persist()
    {
        if (!UiHelpers.IsValid(_panel))
        {
            return;
        }

        Rect2 safeRect = GetSafeRect();
        _preferredSize = SanitizePreferredSize(GetPreferredSizeFallback());
        Vector2 requestedSize = ClampSize(_preferredSize, safeRect);
        Vector2 size = ApplyPanelSize(requestedSize);
        CaptureActualSizeWhenLayoutOverridesRequest(requestedSize, size);
        Vector2 position = ClampAndSnapPosition(_panel.Position, size, safeRect, snap: true);
        _panel.Position = position;
        _lastSafeRect = safeRect;
        _saveLayout(OverlayLayoutState.FromNormalized(NormalizePosition(position, size, safeRect), _preferredSize));
    }

    public void CancelInteraction()
    {
        _dragging = false;
        _resizing = false;
        _resizeEdge = ResizeEdge.None;
        if (UiHelpers.IsValid(_panel))
        {
            _panel.MouseDefaultCursorShape = Control.CursorShape.Arrow;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelInteraction();
        if (UiHelpers.IsValid(_viewport))
        {
            _viewport.SizeChanged -= OnViewportChanged;
        }
        if (UiHelpers.IsValid(_game))
        {
            _game!.WindowChange -= OnViewportChanged;
        }
    }

    private bool HandleMouseButton(InputEventMouseButton mouseButton)
    {
        Vector2 pointer = mouseButton.Position;
        if (mouseButton.Pressed)
        {
            Rect2 panelRect = _panel.GetGlobalRect();
            bool insidePanel = panelRect.HasPoint(pointer);
            bool insideInteraction = IsPointInInteractionArea(pointer);
            if (!insideInteraction)
            {
                return false;
            }

            if (insidePanel && _isInteractionExcluded?.Invoke(pointer) == true)
            {
                return true;
            }

            ResizeEdge edge = _canResize ? GetResizeEdge(pointer, panelRect) : ResizeEdge.None;
            if (edge != ResizeEdge.None)
            {
                _resizing = true;
                _dragging = false;
                _resizeEdge = edge;
                _pointerStart = pointer;
                _panelStartRect = new Rect2(_panel.Position, _panel.Size);
                return true;
            }

            if (insidePanel && UiHelpers.IsValid(_dragHandle) && _dragHandle.GetGlobalRect().HasPoint(pointer))
            {
                _dragging = true;
                _resizing = false;
                _pointerStart = pointer;
                _panelStartRect = new Rect2(_panel.Position, _panel.Size);
            }

            // A click inside belongs to this overlay even when a normal Godot
            // control will perform the actual action.
            return true;
        }

        bool wasInteracting = _dragging || _resizing;
        if (wasInteracting)
        {
            Persist();
        }
        CancelInteraction();
        return wasInteracting;
    }

    private void DragTo(Vector2 pointer)
    {
        Rect2 safeRect = GetSafeRect();
        Vector2 desired = _panelStartRect.Position + (pointer - _pointerStart);
        _panel.Position = ClampAndSnapPosition(desired, _panel.Size, safeRect, snap: true);
        _lastSafeRect = safeRect;
    }

    private void ResizeTo(Vector2 pointer)
    {
        Rect2 safeRect = GetSafeRect();
        Vector2 delta = pointer - _pointerStart;

        float left = _panelStartRect.Position.X;
        float top = _panelStartRect.Position.Y;
        float right = _panelStartRect.End.X;
        float bottom = _panelStartRect.End.Y;

        float minWidth = Math.Min(SanitizePositive(_minimumSize.X, 1f), safeRect.Size.X);
        float minHeight = Math.Min(SanitizePositive(_minimumSize.Y, 1f), safeRect.Size.Y);
        float maxWidth = Math.Max(minWidth, GetMaximumWidth(safeRect));
        float maxHeight = Math.Max(minHeight, GetMaximumHeight(safeRect));

        if (_resizeEdge.HasFlag(ResizeEdge.Left))
        {
            float minimumLeft = Math.Max(safeRect.Position.X, right - maxWidth);
            float maximumLeft = right - minWidth;
            left = Math.Clamp(_panelStartRect.Position.X + delta.X, minimumLeft, maximumLeft);
        }
        if (_resizeEdge.HasFlag(ResizeEdge.Right))
        {
            float minimumRight = left + minWidth;
            float maximumRight = Math.Min(safeRect.End.X, left + maxWidth);
            right = Math.Clamp(_panelStartRect.End.X + delta.X, minimumRight, maximumRight);
        }
        if (_resizeEdge.HasFlag(ResizeEdge.Top))
        {
            float minimumTop = Math.Max(safeRect.Position.Y, bottom - maxHeight);
            float maximumTop = bottom - minHeight;
            top = Math.Clamp(_panelStartRect.Position.Y + delta.Y, minimumTop, maximumTop);
        }
        if (_resizeEdge.HasFlag(ResizeEdge.Bottom))
        {
            float minimumBottom = top + minHeight;
            float maximumBottom = Math.Min(safeRect.End.Y, top + maxHeight);
            bottom = Math.Clamp(_panelStartRect.End.Y + delta.Y, minimumBottom, maximumBottom);
        }

        // Snap only the edge being moved. Snapping the whole panel would shift
        // the opposite edge and make left/top resizing feel unstable.
        if (_resizeEdge.HasFlag(ResizeEdge.Left)
            && Math.Abs(left - safeRect.Position.X) <= _snapDistance
            && right - safeRect.Position.X <= maxWidth)
        {
            left = safeRect.Position.X;
        }
        if (_resizeEdge.HasFlag(ResizeEdge.Right)
            && Math.Abs(right - safeRect.End.X) <= _snapDistance
            && safeRect.End.X - left <= maxWidth)
        {
            right = safeRect.End.X;
        }
        if (_resizeEdge.HasFlag(ResizeEdge.Top)
            && Math.Abs(top - safeRect.Position.Y) <= _snapDistance
            && bottom - safeRect.Position.Y <= maxHeight)
        {
            top = safeRect.Position.Y;
        }
        if (_resizeEdge.HasFlag(ResizeEdge.Bottom)
            && Math.Abs(bottom - safeRect.End.Y) <= _snapDistance
            && safeRect.End.Y - top <= maxHeight)
        {
            bottom = safeRect.End.Y;
        }

        Vector2 requestedSize = new(Math.Max(minWidth, right - left), Math.Max(minHeight, bottom - top));
        Vector2 size = ApplyPanelSize(requestedSize);
        _preferredSize = SanitizePreferredSize(size);
        float effectiveLeft = _resizeEdge.HasFlag(ResizeEdge.Left) ? right - size.X : left;
        float effectiveTop = _resizeEdge.HasFlag(ResizeEdge.Top) ? bottom - size.Y : top;
        _panel.Position = ClampAndSnapPosition(new Vector2(effectiveLeft, effectiveTop), size, safeRect, snap: false);
        _lastSafeRect = safeRect;
    }

    private void OnViewportChanged()
    {
        bool wasInteracting = IsInteracting;
        CancelInteraction();
        ClampToViewport(preserveRelativePosition: true);
        if (wasInteracting) Persist();
    }

    private Rect2 GetSafeRect()
    {
        Vector2 viewportSize = _viewport.GetVisibleRect().Size;
        float marginX = Math.Min(_viewportMargin, Math.Max(0f, viewportSize.X * 0.25f));
        float marginY = Math.Min(_viewportMargin, Math.Max(0f, viewportSize.Y * 0.25f));
        return new Rect2(
            new Vector2(marginX, marginY),
            new Vector2(
                Math.Max(1f, viewportSize.X - marginX * 2f),
                Math.Max(1f, viewportSize.Y - marginY * 2f)));
    }

    private Vector2 ClampSize(Vector2 size, Rect2 safeRect)
    {
        float minWidth = Math.Min(SanitizePositive(_minimumSize.X, 1f), safeRect.Size.X);
        float minHeight = Math.Min(SanitizePositive(_minimumSize.Y, 1f), safeRect.Size.Y);
        float maxWidth = Math.Max(minWidth, GetMaximumWidth(safeRect));
        float maxHeight = Math.Max(minHeight, GetMaximumHeight(safeRect));
        float width = float.IsFinite(size.X) ? size.X : minWidth;
        float height = float.IsFinite(size.Y) ? size.Y : minHeight;
        return new Vector2(Math.Clamp(width, minWidth, maxWidth), Math.Clamp(height, minHeight, maxHeight));
    }

    private Vector2 GetPreferredSizeFallback()
    {
        if (float.IsFinite(_preferredSize.X) && _preferredSize.X > 0f
            && float.IsFinite(_preferredSize.Y) && _preferredSize.Y > 0f)
        {
            return _preferredSize;
        }

        return UiHelpers.IsValid(_panel) ? _panel.Size : _minimumSize;
    }

    private Vector2 SanitizePreferredSize(Vector2 size)
    {
        float minWidth = SanitizePositive(_minimumSize.X, 1f);
        float minHeight = SanitizePositive(_minimumSize.Y, 1f);
        float configuredMaxWidth = float.IsFinite(_maximumSize.X) && _maximumSize.X > 0f
            ? Math.Max(minWidth, _maximumSize.X)
            : float.MaxValue;
        float configuredMaxHeight = float.IsFinite(_maximumSize.Y) && _maximumSize.Y > 0f
            ? Math.Max(minHeight, _maximumSize.Y)
            : float.MaxValue;
        float width = float.IsFinite(size.X) && size.X > 0f ? size.X : minWidth;
        float height = float.IsFinite(size.Y) && size.Y > 0f ? size.Y : minHeight;
        return new Vector2(
            Math.Clamp(width, minWidth, configuredMaxWidth),
            Math.Clamp(height, minHeight, configuredMaxHeight));
    }

    private float GetMaximumWidth(Rect2 safeRect)
    {
        float maximum = float.IsFinite(_maximumSize.X) ? _maximumSize.X : 0f;
        return maximum > 0f ? Math.Min(maximum, safeRect.Size.X) : safeRect.Size.X;
    }

    private float GetMaximumHeight(Rect2 safeRect)
    {
        float maximum = float.IsFinite(_maximumSize.Y) ? _maximumSize.Y : 0f;
        return maximum > 0f ? Math.Min(maximum, safeRect.Size.Y) : safeRect.Size.Y;
    }

    private static float SanitizePositive(float value, float fallback)
    {
        return float.IsFinite(value) && value > 0f ? value : fallback;
    }

    private Vector2 ApplyPanelSize(Vector2 size)
    {
        // CustomMinimumSize is the real minimum, not the current drag size.
        _panel.CustomMinimumSize = new Vector2(
            Math.Min(SanitizePositive(_minimumSize.X, 1f), size.X),
            Math.Min(SanitizePositive(_minimumSize.Y, 1f), size.Y));
        _panel.Size = size;
        return _panel.Size;
    }

    private void CaptureActualSizeWhenLayoutOverridesRequest(Vector2 requestedSize, Vector2 actualSize)
    {
        if (!actualSize.IsEqualApprox(requestedSize))
        {
            _preferredSize = SanitizePreferredSize(actualSize);
        }
    }

    private Vector2 ClampAndSnapPosition(Vector2 position, Vector2 size, Rect2 safeRect, bool snap) => new(
        OverlayAxis.Clamp(position.X, size.X, safeRect.Position.X, safeRect.Size.X, _snapDistance, snap),
        OverlayAxis.Clamp(position.Y, size.Y, safeRect.Position.Y, safeRect.Size.Y, _snapDistance, snap));

    private static Vector2 NormalizePosition(Vector2 position, Vector2 size, Rect2 safeRect) => new(
        OverlayAxis.Normalize(position.X, size.X, safeRect.Position.X, safeRect.Size.X),
        OverlayAxis.Normalize(position.Y, size.Y, safeRect.Position.Y, safeRect.Size.Y));

    private static Vector2 DenormalizePosition(Vector2 normalized, Rect2 safeRect, Vector2 size) => new(
        OverlayAxis.Position(normalized.X, size.X, safeRect.Position.X, safeRect.Size.X),
        OverlayAxis.Position(normalized.Y, size.Y, safeRect.Position.Y, safeRect.Size.Y));

    private ResizeEdge GetResizeEdge(Vector2 pointer, Rect2 rect)
    {
        Rect2 interactionRect = new(
            rect.Position - new Vector2(_resizeMargin, _resizeMargin),
            rect.Size + new Vector2(_resizeMargin * 2f, _resizeMargin * 2f));
        if (!interactionRect.HasPoint(pointer))
        {
            return ResizeEdge.None;
        }

        ResizeEdge edge = ResizeEdge.None;
        if (Math.Abs(pointer.X - rect.Position.X) <= _resizeMargin) edge |= ResizeEdge.Left;
        if (Math.Abs(pointer.X - rect.End.X) <= _resizeMargin) edge |= ResizeEdge.Right;
        if (Math.Abs(pointer.Y - rect.Position.Y) <= _resizeMargin) edge |= ResizeEdge.Top;
        if (Math.Abs(pointer.Y - rect.End.Y) <= _resizeMargin) edge |= ResizeEdge.Bottom;
        return edge;
    }

    private void UpdateResizeCursor(Vector2 pointer, Rect2 rect)
    {
        if (rect.HasPoint(pointer) && _isInteractionExcluded?.Invoke(pointer) == true)
        {
            _panel.MouseDefaultCursorShape = Control.CursorShape.Arrow;
            return;
        }

        if (!_canResize)
        {
            _panel.MouseDefaultCursorShape = UiHelpers.IsValid(_dragHandle) && _dragHandle.GetGlobalRect().HasPoint(pointer)
                ? Control.CursorShape.Move
                : Control.CursorShape.Arrow;
            return;
        }

        ResizeEdge edge = GetResizeEdge(pointer, rect);
        _panel.MouseDefaultCursorShape = edge switch
        {
            ResizeEdge.Left | ResizeEdge.Top => Control.CursorShape.Fdiagsize,
            ResizeEdge.Right | ResizeEdge.Bottom => Control.CursorShape.Fdiagsize,
            ResizeEdge.Right | ResizeEdge.Top => Control.CursorShape.Bdiagsize,
            ResizeEdge.Left | ResizeEdge.Bottom => Control.CursorShape.Bdiagsize,
            ResizeEdge.Left => Control.CursorShape.Hsize,
            ResizeEdge.Right => Control.CursorShape.Hsize,
            ResizeEdge.Top => Control.CursorShape.Vsize,
            ResizeEdge.Bottom => Control.CursorShape.Vsize,
            _ => UiHelpers.IsValid(_dragHandle) && _dragHandle.GetGlobalRect().HasPoint(pointer)
                ? Control.CursorShape.Move
                : Control.CursorShape.Arrow
        };
    }
}
